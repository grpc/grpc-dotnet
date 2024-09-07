#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Diagnostics;
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Shared;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(HttpContextServerCallContextDebugView))]
internal sealed partial class HttpContextServerCallContext : ServerCallContext, IServerCallContextFeature
{
    private static readonly AuthContext UnauthenticatedContext = new AuthContext(null, new Dictionary<string, List<AuthProperty>>());
    private string? _peer;
    private Metadata? _requestHeaders;
    private Metadata? _responseTrailers;
    private Status _status;
    private AuthContext? _authContext;
    private Activity? _activity;
    // Internal for tests
    internal ServerCallDeadlineManager? DeadlineManager;
    private HttpContextSerializationContext? _serializationContext;
    private DefaultDeserializationContext? _deserializationContext;

    internal HttpContextServerCallContext(HttpContext httpContext, MethodOptions options, Type requestType, Type responseType, ILogger logger)
    {
        HttpContext = httpContext;
        Options = options;
        RequestType = requestType;
        ResponseType = responseType;
        Logger = logger;
    }

    internal ILogger Logger { get; }
    internal HttpContext HttpContext { get; }
    internal MethodOptions Options { get; }
    internal Type RequestType { get; }
    internal Type ResponseType { get; }
    internal string? ResponseGrpcEncoding { get; private set; }

    internal HttpContextSerializationContext SerializationContext
    {
        get => _serializationContext ??= new HttpContextSerializationContext(this);
    }
    internal DefaultDeserializationContext DeserializationContext
    {
        get => _deserializationContext ??= new DefaultDeserializationContext();
    }

    internal bool HasResponseTrailers => _responseTrailers != null;

    protected override string MethodCore => HttpContext.Request.Path.Value!;

    protected override string HostCore => HttpContext.Request.Host.Value!;

    protected override string PeerCore
    {
        get
        {
            // Follows the standard at https://github.com/grpc/grpc/blob/master/doc/naming.md
            return _peer ??= BuildPeer();
        }
    }

    private string BuildPeer()
    {
        var connection = HttpContext.Connection;
        if (connection.RemoteIpAddress != null)
        {
            switch (connection.RemoteIpAddress.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return $"ipv4:{connection.RemoteIpAddress}:{connection.RemotePort}";
                case AddressFamily.InterNetworkV6:
                    return $"ipv6:[{connection.RemoteIpAddress}]:{connection.RemotePort}";
                default:
                    // TODO(JamesNK) - Test what should be output when used with UDS and named pipes
                    return $"unknown:{connection.RemoteIpAddress}:{connection.RemotePort}";
            }
        }
        else
        {
            return "unknown"; // Match Grpc.Core
        }
    }

    protected override DateTime DeadlineCore => DeadlineManager?.Deadline ?? DateTime.MaxValue;

    protected override Metadata RequestHeadersCore
    {
        get
        {
            if (_requestHeaders == null)
            {
                _requestHeaders = new Metadata();

                foreach (var header in HttpContext.Request.Headers)
                {
                    if (GrpcProtocolHelpers.ShouldSkipHeader(header.Key))
                    {
                        continue;
                    }

                    if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        _requestHeaders.Add(header.Key, GrpcProtocolHelpers.ParseBinaryHeader(header.Value!));
                    }
                    else
                    {
                        _requestHeaders.Add(header.Key, header.Value!);
                    }
                }
            }

            return _requestHeaders;
        }
    }

    internal Task ProcessHandlerErrorAsync(Exception ex, string method)
    {
        if (DeadlineManager == null)
        {
            ProcessHandlerError(ex, method);
            return Task.CompletedTask;
        }

        // Could have a fast path for no deadline being raised when an error happens,
        // but it isn't worth the complexity.
        return ProcessHandlerErrorAsyncCore(ex, method);
    }

    private async Task ProcessHandlerErrorAsyncCore(Exception ex, string method)
    {
        Debug.Assert(DeadlineManager != null, "Deadline manager should have been created.");

        if (!DeadlineManager.TrySetCallComplete())
        {
            await DeadlineManager.WaitDeadlineCompleteAsync();
        }

        try
        {
            ProcessHandlerError(ex, method);
        }
        finally
        {
            await DeadlineManager.DisposeAsync();
            GrpcServerLog.DeadlineStopped(Logger);
        }
    }

    private void ProcessHandlerError(Exception ex, string method)
    {
        if (ex is RpcException rpcException)
        {
            // RpcException is thrown by client code to modify the status returned from the server.
            // Log the status, detail and debug exception (if present).
            // Don't log the RpcException itself to reduce log verbosity. All of its information is already captured.
            GrpcServerLog.RpcConnectionError(Logger, rpcException.StatusCode, rpcException.Status.Detail, rpcException.Status.DebugException);

            // There are two sources of metadata entries on the server-side:
            // 1. serverCallContext.ResponseTrailers
            // 2. trailers in RpcException thrown by user code in server side handler.
            // As metadata allows duplicate keys, the logical thing to do is
            // to just merge trailers from RpcException into serverCallContext.ResponseTrailers.
            foreach (var entry in rpcException.Trailers)
            {
                ResponseTrailers.Add(entry);
            }

            _status = rpcException.Status;
        }
        else
        {
            if (ex is OperationCanceledException or IOException && CancellationTokenCore.IsCancellationRequested)
            {
                // Request cancellation can cause OCE and IOException.
                // When the request has been canceled log these error types at the info-level to avoid creating error-level noise.
                GrpcServerLog.ServiceMethodCanceled(Logger, method, ex);
            }
            else
            {
                GrpcServerLog.ErrorExecutingServiceMethod(Logger, method, ex);
            }

            var message = ErrorMessageHelper.BuildErrorMessage("Exception was thrown by handler.", ex, Options.EnableDetailedErrors);

            // Note that the exception given to status won't be returned to the client.
            // It is still useful to set in case an interceptor accesses the status on the server.
            _status = new Status(StatusCode.Unknown, message, ex);
        }

        // Don't update trailers if request has exceeded deadline
        if (DeadlineManager == null || !DeadlineManager.IsDeadlineExceededStarted)
        {
            HttpContext.Response.ConsolidateTrailers(this);
        }

        DeadlineManager?.SetCallEnded();

        LogCallEnd();
    }

    // If there is a deadline then we need to have our own cancellation token.
    // Deadline will call CompleteAsync, then Reset/Abort. This order means RequestAborted
    // is not raised, so deadlineCts will be triggered instead.
    protected override CancellationToken CancellationTokenCore => DeadlineManager?.CancellationToken ?? HttpContext.RequestAborted;

    protected override Metadata ResponseTrailersCore
    {
        get
        {
            if (_responseTrailers == null)
            {
                _responseTrailers = new Metadata();
            }

            return _responseTrailers;
        }
    }

    protected override Status StatusCore
    {
        get => _status;
        set => _status = value;
    }

    internal Task EndCallAsync()
    {
        if (DeadlineManager == null)
        {
            EndCallCore();
            return Task.CompletedTask;
        }
        else if (DeadlineManager.TrySetCallComplete())
        {
            // Fast path when deadline hasn't been raised.
            EndCallCore();
            GrpcServerLog.DeadlineStopped(Logger);
            return DeadlineManager.DisposeAsync().AsTask();
        }

        // Deadline is exceeded
        return EndCallAsyncCore();
    }

    private async Task EndCallAsyncCore()
    {
        Debug.Assert(DeadlineManager != null, "Deadline manager should have been created.");

        try
        {
            // Deadline has started
            await DeadlineManager.WaitDeadlineCompleteAsync();

            EndCallCore();
            DeadlineManager.SetCallEnded();
            GrpcServerLog.DeadlineStopped(Logger);
        }
        finally
        {
            await DeadlineManager.DisposeAsync();
        }
    }

    private void EndCallCore()
    {
        // Don't update trailers if request has exceeded deadline
        if (DeadlineManager == null || !DeadlineManager.IsDeadlineExceededStarted)
        {
            HttpContext.Response.ConsolidateTrailers(this);
        }

        LogCallEnd();
    }

    private void LogCallEnd()
    {
        _activity?.AddTag(GrpcServerConstants.ActivityStatusCodeTag, _status.StatusCode.ToTrailerString());
        if (_status.StatusCode != StatusCode.OK)
        {
            if (GrpcEventSource.Log.IsEnabled())
            {
                GrpcEventSource.Log.CallFailed(_status.StatusCode);
            }
        }
        if (GrpcEventSource.Log.IsEnabled())
        {
            GrpcEventSource.Log.CallStop();
        }
    }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore
    {
        get
        {
            if (_authContext == null)
            {
                var clientCertificate = HttpContext.Connection.ClientCertificate;
                if (clientCertificate == null)
                {
                    _authContext = UnauthenticatedContext;
                }
                else
                {
                    _authContext = GrpcProtocolHelpers.CreateAuthContext(clientCertificate);
                }
            }

            return _authContext;
        }
    }

    public ServerCallContext ServerCallContext => this;

    protected override IDictionary<object, object> UserStateCore => HttpContext.Items!;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
    {
        // TODO(JunTaoLuo, JamesNK): Currently blocked on ContextPropagationToken implementation in Grpc.Core.Api
        // https://github.com/grpc/grpc-dotnet/issues/40
        throw new NotImplementedException("CreatePropagationToken will be implemented in a future version.");
    }

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
    {
        ArgumentNullThrowHelper.ThrowIfNull(responseHeaders);

        // Headers can only be written once. Throw on subsequent call to write response header instead of silent no-op.
        if (HttpContext.Response.HasStarted)
        {
            throw new InvalidOperationException("Response headers can only be sent once per call.");
        }

        foreach (var header in responseHeaders)
        {
            if (header.Key == GrpcProtocolConstants.CompressionRequestAlgorithmHeader)
            {
                // grpc-internal-encoding-request is used in the server to set message compression
                // on a per-call bassis.
                // 'grpc-encoding' is sent even if WriteOptions.Flags = NoCompress. In that situation
                // individual messages will not be written with compression.
                ResponseGrpcEncoding = header.Value;
                HttpContext.Response.Headers[GrpcProtocolConstants.MessageEncodingHeader] = ResponseGrpcEncoding;
            }
            else
            {
                var encodedValue = header.IsBinary ? Convert.ToBase64String(header.ValueBytes) : header.Value;
                try
                {
                    HttpContext.Response.Headers.Append(header.Key, encodedValue);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error adding response header '{header.Key}'.", ex);
                }
            }
        }

        return HttpContext.Response.BodyWriter.FlushAsync().GetAsTask();
    }

    // Clock is for testing
    public void Initialize(ISystemClock? clock = null)
    {
        _activity = GetHostActivity();
        _activity?.AddTag(GrpcServerConstants.ActivityMethodTag, MethodCore);

        if (GrpcEventSource.Log.IsEnabled())
        {
            GrpcEventSource.Log.CallStart(MethodCore);
        }

        var timeout = GetTimeout();

        if (timeout != TimeSpan.Zero)
        {
            DeadlineManager = new ServerCallDeadlineManager(this, clock ?? SystemClock.Instance, timeout);
            GrpcServerLog.DeadlineStarted(Logger, timeout);
        }

        var serviceDefaultCompression = Options.ResponseCompressionAlgorithm;
        if (serviceDefaultCompression != null &&
            !GrpcProtocolConstants.IsGrpcEncodingIdentity(serviceDefaultCompression) &&
            IsEncodingInRequestAcceptEncoding(serviceDefaultCompression))
        {
            ResponseGrpcEncoding = serviceDefaultCompression;
        }

        // grpc-encoding response header is optional and is inferred as 'identity' when not present.
        // Only write a non-identity value for performance.
        if (ResponseGrpcEncoding != null)
        {
            HttpContext.Response.Headers[GrpcProtocolConstants.MessageEncodingHeader] = ResponseGrpcEncoding;
        }
    }

    private Activity? GetHostActivity()
    {
        // Feature always returns the host activity
        var feature = HttpContext.Features.Get<IHttpActivityFeature>();
        if (feature != null)
        {
            return feature.Activity;
        }

        // If feature isn't available, or not supported, then fallback to Activity.Current.
        var activity = Activity.Current;
        while (activity != null)
        {
            // We only want to add gRPC metadata to the host activity
            // Search parent activities in case a new activity was started in middleware before gRPC endpoint is invoked
            if (string.Equals(activity.OperationName, GrpcServerConstants.HostActivityName, StringComparison.Ordinal))
            {
                return activity;
            }

            activity = activity.Parent;
        }

        return null;
    }

    private TimeSpan GetTimeout()
    {
        if (HttpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.TimeoutHeader, out var values))
        {
            if (GrpcProtocolHelpers.TryDecodeTimeout(values, out var timeout) &&
                timeout > TimeSpan.Zero)
            {
                if (timeout.Ticks > GrpcProtocolConstants.MaxDeadlineTicks)
                {
                    GrpcServerLog.DeadlineTimeoutTooLong(Logger, timeout);

                    timeout = TimeSpan.FromTicks(GrpcProtocolConstants.MaxDeadlineTicks);
                }

                return timeout;
            }

            GrpcServerLog.InvalidTimeoutIgnored(Logger, values!);
        }

        return TimeSpan.Zero;
    }

    internal async Task DeadlineExceededAsync()
    {
        GrpcServerLog.DeadlineExceeded(Logger, GetTimeout());
        if (GrpcEventSource.Log.IsEnabled())
        {
            GrpcEventSource.Log.CallDeadlineExceeded();
        }

        var status = new Status(StatusCode.DeadlineExceeded, "Deadline Exceeded");

        var trailersDestination = GrpcProtocolHelpers.GetTrailersDestination(HttpContext.Response);
        GrpcProtocolHelpers.SetStatus(trailersDestination, status);

        _status = status;

        // Immediately send remaining response content and trailers
        // If feature is null then reset/abort will still end request, but response won't have trailers
        var completionFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        if (completionFeature != null)
        {
            await completionFeature.CompleteAsync();
        }

        CancelRequest();
    }

    internal void CancelRequest()
    {
        // HttpResetFeature should always be set on context,
        // but in case it isn't, fall back to HttpContext.Abort.
        // Abort will send error code INTERNAL_ERROR.
        var resetFeature = HttpContext.Features.Get<IHttpResetFeature>();
        if (resetFeature != null)
        {
            var errorCode = GrpcProtocolConstants.GetCancelErrorCode(HttpContext.Request.Protocol);

            GrpcServerLog.ResettingResponse(Logger, errorCode);
            resetFeature.Reset(errorCode);
        }
        else
        {
            // Note that some clients will fail with error code INTERNAL_ERROR.
            GrpcServerLog.AbortingResponse(Logger);
            HttpContext.Abort();
        }
    }

    internal string? GetRequestGrpcEncoding()
    {
        if (HttpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.MessageEncodingHeader, out var values))
        {
            return values;
        }

        return null;
    }

    internal bool IsEncodingInRequestAcceptEncoding(string encoding)
    {
        if (HttpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.MessageAcceptEncodingHeader, out var values))
        {
            var acceptEncoding = values.ToString().AsSpan();

            while (true)
            {
                var separatorIndex = acceptEncoding.IndexOf(',');

                ReadOnlySpan<char> segment;
                if (separatorIndex != -1)
                {
                    segment = acceptEncoding.Slice(0, separatorIndex);
                    acceptEncoding = acceptEncoding.Slice(separatorIndex + 1);
                }
                else
                {
                    segment = acceptEncoding;
                }

                segment = segment.Trim();

                // Check segment
                if (segment.SequenceEqual(encoding))
                {
                    return true;
                }

                if (separatorIndex == -1)
                {
                    break;
                }
            }

            // Check remainder
            if (acceptEncoding.SequenceEqual(encoding))
            {
                return true;
            }
        }

        return false;
    }

    internal void ValidateAcceptEncodingContainsResponseEncoding()
    {
        var resolvedResponseGrpcEncoding = ResponseGrpcEncoding ?? GrpcProtocolConstants.IdentityGrpcEncoding;

        if (!IsEncodingInRequestAcceptEncoding(resolvedResponseGrpcEncoding))
        {
            GrpcServerLog.EncodingNotInAcceptEncoding(Logger, resolvedResponseGrpcEncoding);
        }
    }

    private string DebuggerToString() => $"Method = {Method}";

    private sealed class HttpContextServerCallContextDebugView
    {
        private readonly HttpContextServerCallContext _context;

        public HttpContextServerCallContextDebugView(HttpContextServerCallContext context)
        {
            _context = context;
        }

        public AuthContext AuthContext => _context.AuthContext;
        public CancellationToken CancellationToken => _context.CancellationToken;
        public DateTime Deadline => _context.Deadline;
        public string Host => _context.Host;
        public string Method => _context.Method;
        public string Peer => _context.Peer;
        public Metadata RequestHeaders => _context.RequestHeaders;
        public Metadata ResponseTrailers => _context.ResponseTrailers;
        public Status Status => _context.Status;
        public IDictionary<object, object> UserState => _context.UserState;
        public WriteOptions? WriteOptions => _context.WriteOptions;

        public HttpContext HttpContext => _context.HttpContext;
    }
}
