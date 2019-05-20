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
using Grpc.AspNetCore.Server.Features;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal sealed partial class HttpContextServerCallContext : ServerCallContext, IDisposable, IServerCallContextFeature
    {
        private static readonly AuthContext UnauthenticatedContext = new AuthContext(null, new Dictionary<string, List<AuthProperty>>());
        private readonly ILogger _logger;

        // Override the current time for unit testing
        internal ISystemClock Clock = SystemClock.Instance;

        private string? _peer;
        private Metadata? _requestHeaders;
        private Metadata? _responseTrailers;
        private DateTime _deadline;
        private Timer? _deadlineTimer;
        private Status _status;
        private AuthContext? _authContext;

        internal HttpContextServerCallContext(HttpContext httpContext, GrpcServiceOptions serviceOptions, ILogger logger)
        {
            HttpContext = httpContext;
            ServiceOptions = serviceOptions;
            _logger = logger;
        }

        internal HttpContext HttpContext { get; }
        internal GrpcServiceOptions ServiceOptions { get; }
        internal string? ResponseGrpcEncoding { get; private set; }

        internal bool HasResponseTrailers => _responseTrailers != null;

        protected override string? MethodCore => HttpContext.Request.Path.Value;

        protected override string? HostCore => HttpContext.Request.Host.Value;

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

        protected override DateTime DeadlineCore => _deadline;

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

        internal void ProcessHandlerError(Exception ex, string method)
        {
            if (ex is RpcException rpcException)
            {
                Log.RpcConnectionError(_logger, rpcException.StatusCode, ex);

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
                Log.ErrorExecutingServiceMethod(_logger, method, ex);

                var message = ErrorMessageHelper.BuildErrorMessage("Exception was thrown by handler.", ex, ServiceOptions.EnableDetailedErrors);
                _status = new Status(StatusCode.Unknown, message);
            }

            HttpContext.Response.ConsolidateTrailers(this);
        }

        protected override CancellationToken CancellationTokenCore => HttpContext.RequestAborted;

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
            HttpContext.Response.ConsolidateTrailers(this);

            if (HasBufferedMessage)
            {
                // Flush any buffered content
                return HttpContext.Response.BodyWriter.FlushAsync().GetAsTask();
            }
            else
            {
                return Task.CompletedTask;
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

            return HttpContext.Response.Body.FlushAsync();
        }

        public void Initialize()
        {
            var timeout = GetTimeout();

            if (timeout != TimeSpan.Zero)
            {
                _deadline = Clock.UtcNow.Add(timeout);

                _deadlineTimer = new Timer(DeadlineExceeded, timeout, timeout, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _deadline = DateTime.MaxValue;
            }

            var serviceDefaultCompression = ServiceOptions.ResponseCompressionAlgorithm;
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

                Log.InvalidTimeoutIgnored(_logger, values);
            }

            return TimeSpan.Zero;
        }

        private void DeadlineExceeded(object state)
        {
            Log.DeadlineExceeded(_logger, (TimeSpan)state);

            _status = new Status(StatusCode.DeadlineExceeded, "Deadline Exceeded");

            try
            {
                // TODO(JamesNK): I believe this sends a RST_STREAM with INTERNAL_ERROR. Grpc.Core sends NO_ERROR
                HttpContext.Abort();
            }
            catch (Exception ex)
            {
                Log.DeadlineCancellationError(_logger, ex);
            }
        }

        public void Dispose()
        {
            _deadlineTimer?.Dispose();
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
                    if (separatorIndex == -1)
                    {
                        break;
                    }

                    var segment = acceptEncoding.Slice(0, separatorIndex);
                    acceptEncoding = acceptEncoding.Slice(separatorIndex);

                    // Check segment
                    if (segment.SequenceEqual(encoding))
                    {
                        return true;
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
                Log.EncodingNotInAcceptEncoding(_logger, ResponseGrpcEncoding);
            }
        }

        internal bool CanWriteCompressed()
        {
            var canCompress = ((WriteOptions?.Flags ?? default) & WriteFlags.NoCompress) != WriteFlags.NoCompress;

            return canCompress;
        }
    }
}
