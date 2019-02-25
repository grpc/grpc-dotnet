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
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal sealed partial class HttpContextServerCallContext : ServerCallContext, IDisposable
    {
        private readonly ILogger _logger;

        // Override the current time for unit testing
        internal ISystemClock Clock = SystemClock.Instance;

        private string _peer;
        private Metadata _requestHeaders;
        private Metadata _responseTrailers;
        private DateTime _deadline;
        private Timer _deadlineTimer;
        private Status _status;

        internal HttpContextServerCallContext(HttpContext httpContext, GrpcServiceOptions serviceOptions, ILogger logger)
        {
            HttpContext = httpContext;
            ServiceOptions = serviceOptions;
            _logger = logger;
        }

        internal HttpContext HttpContext { get; }
        internal GrpcServiceOptions ServiceOptions { get; }

        internal bool HasResponseTrailers => _responseTrailers != null;

        protected override string MethodCore => HttpContext.Request.Path.Value;

        protected override string HostCore => HttpContext.Request.Host.Value;

        protected override string PeerCore
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
                        // ASP.NET Core includes pseudo headers in the set of request headers
                        // whereas, they are not in gRPC implementations. We will filter them
                        // out when we construct the list of headers on the context.
                        if (header.Key.StartsWith(':'))
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
                RpcConnectionError(_logger, rpcException.StatusCode, ex);

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
                ErrorExecutingServiceMethod(_logger, method, ex);

                var message = ErrorMessageHelper.BuildErrorMessage("Exception was thrown by handler.", ex, ServiceOptions.EnableDetailedErrors);
                _status = new Status(StatusCode.Unknown, message);
            }
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

        protected override WriteOptions WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore
        {
            get
            {
                // TODO(JunTaoLuo, JamesNK): Currently blocked on AuthContext constructor being internal
                // https://github.com/grpc/grpc-dotnet/issues/72
                throw new NotImplementedException("AuthContext will be implemented in a future version.");
            }
        }

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

                InvalidTimeoutIgnored(_logger, values);
            }

            return TimeSpan.Zero;
        }

        private void DeadlineExceeded(object state)
        {
            DeadlineExceeded(_logger, (TimeSpan)state);

            _status = new Status(StatusCode.DeadlineExceeded, "Deadline Exceeded");

            // TODO(JamesNK): I believe this sends a RST_STREAM with INTERNAL_ERROR. Grpc.Core sends NO_ERROR
            HttpContext.Abort();
        }

        public void Dispose()
        {
            _deadlineTimer?.Dispose();
        }
    }
}
