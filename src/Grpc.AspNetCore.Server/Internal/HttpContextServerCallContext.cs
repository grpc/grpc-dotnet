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

namespace Grpc.AspNetCore.Server.Internal
{
    internal sealed class HttpContextServerCallContext : ServerCallContext, IDisposable
    {
        // Override the current time for unit testing
        internal ISystemClock Clock = SystemClock.Instance;
        private string _peer;
        private Metadata _requestHeaders;
        private Metadata _responseTrailers;
        private DateTime _deadline;
        private CancellationTokenSource _cts;

        internal HttpContextServerCallContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        internal HttpContext HttpContext { get; }

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
                            _requestHeaders.Add(header.Key, Convert.FromBase64String(header.Value));
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

        protected override CancellationToken CancellationTokenCore => _cts.Token;

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

        protected override Status StatusCore { get; set; }

        protected override WriteOptions WriteOptionsCore { get; set; }

        // TODO(JunTaoLuo, JamesNK): implement this
        protected override AuthContext AuthContextCore => throw new NotImplementedException();

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
                // CancellationTokenSource does not support greater than int.MaxValue milliseconds
                if (timeout.TotalMilliseconds > int.MaxValue)
                {
                    throw new InvalidOperationException("A timeout greater than 2147483647 milliseconds is not supported.");
                }

                _deadline = Clock.UtcNow.Add(timeout);
                _cts = new CancellationTokenSource(timeout);
            }
            else
            {
                _deadline = DateTime.MaxValue;
                _cts = new CancellationTokenSource();
            }
        }

        private TimeSpan GetTimeout()
        {
            if (HttpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.TimeoutHeader, out var values))
            {
                if (GrpcProtocolHelpers.TryDecodeTimeout(values, out var timeout))
                {
                    return timeout;
                }

                // TODO(JamesNK): Log that the bad timeout value is being ignored
                // https://github.com/grpc/grpc/blob/da09b1fd083a80e3ebca927eb5ff6bc2cfe23cb5/src/core/ext/transport/chttp2/transport/parsing.cc#L441
            }

            return TimeSpan.Zero;
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
