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
using System.Threading.Tasks;
using Grpc.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Web.Internal
{
    internal sealed class GrpcWebMiddleware
    {
        private readonly GrpcWebOptions _options;
        private readonly ILogger<GrpcWebMiddleware> _logger;
        private readonly RequestDelegate _next;

        public GrpcWebMiddleware(IOptions<GrpcWebOptions> options, ILogger<GrpcWebMiddleware> logger, RequestDelegate next)
        {
            _options = options.Value;
            _logger = logger;
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            var mode = GetGrpcWebMode(httpContext);
            if (mode != ServerGrpcWebMode.None)
            {
                Log.DetectedGrpcWebRequest(_logger, httpContext.Request.ContentType!);

                var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<IGrpcWebEnabledMetadata>();
                if (metadata?.GrpcWebEnabled ?? _options.DefaultEnabled)
                {
                    return HandleGrpcWebRequest(httpContext, mode);
                }

                Log.GrpcWebRequestNotProcessed(_logger);
            }

            return _next(httpContext);
        }

        private async Task HandleGrpcWebRequest(HttpContext httpContext, ServerGrpcWebMode mode)
        {
            var feature = new GrpcWebFeature(mode, httpContext);

            var initialProtocol = httpContext.Request.Protocol;

            // Modifying the request is required to stop Grpc.AspNetCore.Server from rejecting it
            httpContext.Request.Protocol = GrpcWebProtocolConstants.Http2Protocol;
            httpContext.Request.ContentType = ResolveContentType(GrpcWebProtocolConstants.GrpcContentType, httpContext.Request.ContentType!);

            // Update response content type back to gRPC-Web
            httpContext.Response.OnStarting(() =>
            {
                // Reset request protocol back to its original value. Not doing this causes a 2 second
                // delay when making HTTP/1.1 calls.
                httpContext.Request.Protocol = initialProtocol;

                if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcContentType, httpContext.Response.ContentType!))
                {
                    var contentType = mode == ServerGrpcWebMode.GrpcWeb
                        ? GrpcWebProtocolConstants.GrpcWebContentType
                        : GrpcWebProtocolConstants.GrpcWebTextContentType;
                    var responseContentType = ResolveContentType(contentType, httpContext.Response.ContentType);

                    httpContext.Response.ContentType = responseContentType;
                    Log.SendingGrpcWebResponse(_logger, responseContentType);
                }

                return Task.CompletedTask;
            });

            try
            {
                await _next(httpContext);

                // If trailers have already been written in CompleteAsync then this will no-op
                await feature.WriteTrailersAsync();
            }
            finally
            {
                feature.DetachFromContext(httpContext);
            }
        }

        private static string ResolveContentType(string newContentType, string originalContentType)
        {
            var contentSuffixIndex = originalContentType.IndexOf('+', StringComparison.Ordinal);
            if (contentSuffixIndex != -1)
            {
                newContentType += originalContentType.Substring(contentSuffixIndex);
            }

            return newContentType;
        }

        internal static ServerGrpcWebMode GetGrpcWebMode(HttpContext httpContext)
        {
            if (HttpMethods.IsPost(httpContext.Request.Method))
            {
                if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebContentType, httpContext.Request.ContentType))
                {
                    return ServerGrpcWebMode.GrpcWeb;
                }
                else if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebTextContentType, httpContext.Request.ContentType))
                {
                    return ServerGrpcWebMode.GrpcWebText;
                }
            }
            
            return ServerGrpcWebMode.None;
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _detectedGrpcWebRequest =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "DetectedGrpcWebRequest"), "Detected gRPC-Web request from content-type '{ContentType}'.");

            private static readonly Action<ILogger, Exception?> _grpcWebRequestNotProcessed =
                LoggerMessage.Define(LogLevel.Debug, new EventId(2, "GrpcWebRequestNotProcessed"), $"gRPC-Web request not processed. gRPC-Web must be enabled by placing the [EnableGrpcWeb] attribute on a service or method, or enable for all services in the app with {nameof(GrpcWebOptions)}.");

            private static readonly Action<ILogger, string, Exception?> _sendingGrpcWebResponse =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(3, "SendingGrpcWebResponse"), "Sending gRPC-Web response with content-type '{ContentType}'.");

            public static void DetectedGrpcWebRequest(ILogger<GrpcWebMiddleware> logger, string contentType)
            {
                _detectedGrpcWebRequest(logger, contentType, null);
            }

            public static void GrpcWebRequestNotProcessed(ILogger<GrpcWebMiddleware> logger)
            {
                _grpcWebRequestNotProcessed(logger, null);
            }

            public static void SendingGrpcWebResponse(ILogger<GrpcWebMiddleware> logger, string contentType)
            {
                _sendingGrpcWebResponse(logger, contentType, null);
            }
        }
    }
}
