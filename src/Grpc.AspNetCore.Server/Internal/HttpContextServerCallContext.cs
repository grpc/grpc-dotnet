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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal sealed partial class HttpContextServerCallContext : ServerCallContext, IServerCallContextFeature
    {
        private static readonly AuthContext UnauthenticatedContext = new AuthContext(null, new Dictionary<string, List<AuthProperty>>());
        private string? _peer;
        private Metadata? _requestHeaders;
        private Metadata? _responseTrailers;
        private Status _status;
        private AuthContext? _authContext;
        // Internal for tests
        internal ServerCallDeadlineManager? DeadlineManager;
        private HttpContextSerializationContext? _serializationContext;
        private DefaultDeserializationContext? _deserializationContext;

        internal HttpContextServerCallContext(HttpContext httpContext, MethodContext methodContext, ILogger logger)
        {
            HttpContext = httpContext;
            MethodContext = methodContext;
            Logger = logger;
        }

        internal ILogger Logger { get; }
        internal HttpContext HttpContext { get; }
        internal MethodContext MethodContext { get; }
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

        protected override string MethodCore => HttpContext.Request.Path.Value;

        protected override string HostCore => HttpContext.Request.Host.Value;

        protected override string? PeerCore
        {
            get
            {
                if (_peer == null)
                {
                    var connection = HttpContext.Connection;
                    if (connection.RemoteIpAddress != null)
                    {
                        _peer = (connection.RemoteIpAddress.AddressFamily == AddressFamily.InterNetwork ? "ipv4:" : "ipv6:") + connection.RemoteIpAddress + ":" + connection.RemotePort;
                    }
                }

                return _peer;
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
                        // gRPC metadata contains a subset of the request headers
                        // Filter out pseudo headers (start with :) and other known headers
                        if (header.Key.StartsWith(':') || GrpcProtocolConstants.FilteredHeaders.Contains(header.Key))
                        {
                            continue;
                        }
                        else if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            _requestHeaders.Add(header.Key, GrpcProtocolHelpers.ParseBinaryHeader(header.Value));
                        }
                        else
                        {
                            _requestHeaders.Add(header.Key, header.Value);
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

            return ProcessHandlerErrorAsyncCore(ex, method);
        }

        private async Task ProcessHandlerErrorAsyncCore(Exception ex, string method)
        {
            Debug.Assert(DeadlineManager != null, "Deadline manager should have been created.");

            await DeadlineManager.Lock.WaitAsync();

            try
            {
                ProcessHandlerError(ex, method);
            }
            finally
            {
                DeadlineManager.Lock.Release();
                await DeadlineManager.DisposeAsync();
            }
        }

        private void ProcessHandlerError(Exception ex, string method)
        {
            if (ex is RpcException rpcException)
            {
                GrpcServerLog.RpcConnectionError(Logger, rpcException.StatusCode, ex);

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
                GrpcServerLog.ErrorExecutingServiceMethod(Logger, method, ex);

                var message = ErrorMessageHelper.BuildErrorMessage("Exception was thrown by handler.", ex, MethodContext.EnableDetailedErrors);
                _status = new Status(StatusCode.Unknown, message);
            }

            // Don't update trailers if request has exceeded deadline/aborted
            if (!CancellationToken.IsCancellationRequested)
            {
                HttpContext.Response.ConsolidateTrailers(this);
            }

            LogCallEnd();

            DeadlineManager?.SetCallComplete();
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

            var lockTask = DeadlineManager.Lock.WaitAsync();
            if (lockTask.IsCompletedSuccessfully)
            {
                Task disposeTask;
                try
                {
                    EndCallCore();
                }
                finally
                {
                    DeadlineManager.Lock.Release();

                    // Can't return from a finally
                    disposeTask = DeadlineManager.DisposeAsync().AsTask();
                }

                return disposeTask;
            }
            else
            {
                return EndCallAsyncCore(lockTask);
            }
        }

        private async Task EndCallAsyncCore(Task lockTask)
        {
            Debug.Assert(DeadlineManager != null, "Deadline manager should have been created.");

            await lockTask;

            try
            {
                EndCallCore();
            }
            finally
            {
                DeadlineManager.Lock.Release();
                await DeadlineManager.DisposeAsync();
            }
        }

        private void EndCallCore()
        {
            // Don't set trailers if deadline exceeded or request aborted
            if (!CancellationToken.IsCancellationRequested)
            {
                HttpContext.Response.ConsolidateTrailers(this);
            }

            LogCallEnd();

            DeadlineManager?.SetCallComplete();
        }

        private void LogCallEnd()
        {
            var activity = GetHostActivity();
            if (activity != null)
            {
                activity.AddTag(GrpcServerConstants.ActivityStatusCodeTag, _status.StatusCode.ToTrailerString());
            }
            if (_status.StatusCode != StatusCode.OK)
            {
                GrpcEventSource.Log.CallFailed(_status.StatusCode);
            }
            GrpcEventSource.Log.CallStop();
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

        protected override IDictionary<object, object> UserStateCore => HttpContext.Items;

        internal bool HasBufferedMessage { get; set; }

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions options)
        {
            // TODO(JunTaoLuo, JamesNK): Currently blocked on ContextPropagationToken implementation in Grpc.Core.Api
            // https://github.com/grpc/grpc-dotnet/issues/40
            throw new NotImplementedException("CreatePropagationToken will be implemented in a future version.");
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            // Headers can only be written once. Throw on subsequent call to write response header instead of silent no-op.
            if (HttpContext.Response.HasStarted)
            {
                throw new InvalidOperationException("Response headers can only be sent once per call.");
            }

            if (responseHeaders != null)
            {
                foreach (var entry in responseHeaders)
                {
                    if (entry.Key == GrpcProtocolConstants.CompressionRequestAlgorithmHeader)
                    {
                        // grpc-internal-encoding-request is used in the server to set message compression
                        // on a per-call bassis.
                        // 'grpc-encoding' is sent even if WriteOptions.Flags = NoCompress. In that situation
                        // individual messages will not be written with compression.
                        ResponseGrpcEncoding = entry.Value;
                        HttpContext.Response.Headers[GrpcProtocolConstants.MessageEncodingHeader] = ResponseGrpcEncoding;
                    }
                    else
                    {
                        if (entry.IsBinary)
                        {
                            HttpContext.Response.Headers[entry.Key] = Convert.ToBase64String(entry.ValueBytes);
                        }
                        else
                        {
                            HttpContext.Response.Headers[entry.Key] = entry.Value;
                        }
                    }
                }
            }

            return HttpContext.Response.BodyWriter.FlushAsync().GetAsTask();
        }

        // Clock is for testing
        public void Initialize(ISystemClock? clock = null)
        {
            var activity = GetHostActivity();
            if (activity != null)
            {
                activity.AddTag(GrpcServerConstants.ActivityMethodTag, MethodCore);
            }

            GrpcEventSource.Log.CallStart(MethodCore);

            var timeout = GetTimeout();

            if (timeout != TimeSpan.Zero)
            {
                DeadlineManager = new ServerCallDeadlineManager(clock ?? SystemClock.Instance, timeout, DeadlineExceededAsync, HttpContext.RequestAborted);
            }

            var serviceDefaultCompression = MethodContext.ResponseCompressionAlgorithm;
            if (serviceDefaultCompression != null &&
                !string.Equals(serviceDefaultCompression, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal) &&
                IsEncodingInRequestAcceptEncoding(serviceDefaultCompression))
            {
                ResponseGrpcEncoding = serviceDefaultCompression;
            }
            else
            {
                ResponseGrpcEncoding = GrpcProtocolConstants.IdentityGrpcEncoding;
            }

            HttpContext.Response.Headers.Append(GrpcProtocolConstants.MessageEncodingHeader, ResponseGrpcEncoding);
        }

        private Activity? GetHostActivity()
        {
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
                // CancellationTokenSource does not support greater than int.MaxValue milliseconds
                if (GrpcProtocolHelpers.TryDecodeTimeout(values, out var timeout) &&
                    timeout > TimeSpan.Zero &&
                    timeout.TotalMilliseconds <= int.MaxValue)
                {
                    return timeout;
                }

                GrpcServerLog.InvalidTimeoutIgnored(Logger, values);
            }

            return TimeSpan.Zero;
        }

        private async Task DeadlineExceededAsync()
        {
            try
            {
                GrpcServerLog.DeadlineExceeded(Logger, GetTimeout());
                GrpcEventSource.Log.CallDeadlineExceeded();

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

                // HttpResetFeature should always be set on context,
                // but in case it isn't, fall back to HttpContext.Abort.
                // Abort will send error code INTERNAL_ERROR instead of NO_ERROR.
                var resetFeature = HttpContext.Features.Get<IHttpResetFeature>();
                if (resetFeature != null)
                {
                    GrpcServerLog.ResettingResponse(Logger, GrpcProtocolConstants.ResetStreamNoError);
                    resetFeature.Reset(GrpcProtocolConstants.ResetStreamNoError);
                }
                else
                {
                    // Note that some clients will fail with error code INTERNAL_ERROR.
                    GrpcServerLog.AbortingResponse(Logger);
                    HttpContext.Abort();
                }
            }
            catch (Exception ex)
            {
                GrpcServerLog.DeadlineCancellationError(Logger, ex);
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
            Debug.Assert(ResponseGrpcEncoding != null);

            if (!IsEncodingInRequestAcceptEncoding(ResponseGrpcEncoding))
            {
                GrpcServerLog.EncodingNotInAcceptEncoding(Logger, ResponseGrpcEncoding);
            }
        }
    }
}
