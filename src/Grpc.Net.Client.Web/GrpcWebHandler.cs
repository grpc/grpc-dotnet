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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client.Web.Internal;
using Grpc.Shared;

namespace Grpc.Net.Client.Web
{
    /// <summary>
    /// A <see cref="DelegatingHandler"/> implementation that executes gRPC-Web request processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This message handler implementation should be used with the .NET client for gRPC to make gRPC-Web calls.
    /// </para>
    /// </remarks>
    public sealed class GrpcWebHandler : DelegatingHandler
    {
        private readonly GrpcWebMode _mode;
        private readonly Version? _httpVersion;

        /// <summary>
        /// Creates a new instance of <see cref="GrpcWebHandler"/>.
        /// </summary>
        /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
        public GrpcWebHandler(GrpcWebMode mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Creates a new instance of <see cref="GrpcWebHandler"/>.
        /// </summary>
        /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
        /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
        public GrpcWebHandler(GrpcWebMode mode, HttpMessageHandler innerHandler) : base(innerHandler)
        {
            _mode = mode;
        }

        /// <summary>
        /// Creates a new instance of <see cref="GrpcWebHandler"/>.
        /// </summary>
        /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
        /// <param name="httpVersion">The HTTP version to used when making gRPC-Web calls.</param>
        public GrpcWebHandler(GrpcWebMode mode, Version httpVersion)
        {
            if (httpVersion == null)
            {
                throw new ArgumentNullException(nameof(httpVersion));
            }

            _mode = mode;
            _httpVersion = httpVersion;
        }

        /// <summary>
        /// Creates a new instance of <see cref="GrpcWebHandler"/>.
        /// </summary>
        /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
        /// <param name="httpVersion">The HTTP version to used when making gRPC-Web calls.</param>
        /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
        public GrpcWebHandler(GrpcWebMode mode, Version httpVersion, HttpMessageHandler innerHandler) : base(innerHandler)
        {
            if (httpVersion == null)
            {
                throw new ArgumentNullException(nameof(httpVersion));
            }

            _mode = mode;
            _httpVersion = httpVersion;
        }

        /// <summary>
        /// Sends an HTTP request to the inner handler to send to the server as an asynchronous operation.
        /// </summary>
        /// <param name="request">The HTTP request message to send to the server.</param>
        /// <param name="cancellationToken">A cancellation token to cancel operation.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcContentType, request.Content?.Headers.ContentType?.MediaType))
            {
                return SendAsyncCore(request, cancellationToken);
            }

            return base.SendAsync(request, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendAsyncCore(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Content = new GrpcWebRequestContent(request.Content, _mode);
            if (_httpVersion != null)
            {
                // This doesn't guarantee that the specified version is used. Some handlers will ignore it.
                // For example, version in the browser always negotiated by the browser and HttpClient
                // uses what the browser has negotiated.
                request.Version = _httpVersion;
            }

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (IsMatchingResponseContentType(_mode, response.Content.Headers.ContentType?.MediaType))
            {
                response.Content = new GrpcWebResponseContent(response.Content, _mode, response.TrailingHeaders);
            }

            // The gRPC client validates HTTP version 2.0 and will error if it isn't. Always set
            // the version to 2.0, even for non-gRPC content type. The client maps HTTP status codes
            // to gRPC statuses, e.g. HTTP 404 -> gRPC unimplemented.
            //
            // Note: Some handlers don't correctly set HttpResponseMessage.Version.
            // We can't rely on it being set correctly. It is safest to always set it to 2.0.
            response.Version = HttpVersion.Version20;

            return response;
        }

        private static bool IsMatchingResponseContentType(GrpcWebMode mode, string? contentType)
        {
            if (mode == GrpcWebMode.GrpcWeb)
            {
                return CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebContentType, contentType);
            }

            if (mode == GrpcWebMode.GrpcWebText)
            {
                return CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebTextContentType, contentType);
            }

            return false;
        }
    }
}
