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

using Grpc.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Web.Internal;

internal sealed partial class GrpcWebMiddleware
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
        var grcpWebContext = GetGrpcWebContext(httpContext);
        if (grcpWebContext.Request != ServerGrpcWebMode.None)
        {
            Log.DetectedGrpcWebRequest(_logger, httpContext.Request.ContentType!);

            var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<IGrpcWebEnabledMetadata>();
            if (metadata?.GrpcWebEnabled ?? _options.DefaultEnabled)
            {
                return HandleGrpcWebRequest(httpContext, grcpWebContext);
            }

            Log.GrpcWebRequestNotProcessed(_logger);
        }

        return _next(httpContext);
    }

    private async Task HandleGrpcWebRequest(HttpContext httpContext, ServerGrpcWebContext grcpWebContext)
    {
        var feature = new GrpcWebFeature(grcpWebContext, httpContext);

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
                var contentType = grcpWebContext.Response == ServerGrpcWebMode.GrpcWeb
                    ? GrpcWebProtocolConstants.GrpcWebContentType
                    : GrpcWebProtocolConstants.GrpcWebTextContentType;
                var responseContentType = ResolveContentType(contentType, httpContext.Response.ContentType!);

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

    internal static ServerGrpcWebContext GetGrpcWebContext(HttpContext httpContext)
    {
        // gRPC requests are always POST.
        if (!HttpMethods.IsPost(httpContext.Request.Method))
        {
            return default;
        }

        // Only run middleware for 'application/grpc-web' or 'application/grpc-web-text'.
        if (!TryGetWebMode(httpContext.Request.ContentType, out var requestMode))
        {
            return default;
        }

        if (TryGetWebMode(httpContext.Request.Headers["Accept"], out var responseMode))
        {
            // gRPC-Web request and response types are typically the same.
            // That means 'application/grpc-web-text' requests also have an 'accept' header value of 'application/grpc-web-text'.
            return new ServerGrpcWebContext(requestMode, responseMode);
        }
        else
        {
            // If there isn't a request 'accept' header then default to mode to 'application/grpc`.
            return new ServerGrpcWebContext(requestMode, ServerGrpcWebMode.GrpcWeb);
        }
    }

    private static bool TryGetWebMode(string? contentType, out ServerGrpcWebMode mode)
    {
        if (!string.IsNullOrEmpty(contentType))
        {
            if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebContentType, contentType))
            {
                mode = ServerGrpcWebMode.GrpcWeb;
                return true;
            }
            else if (CommonGrpcProtocolHelpers.IsContentType(GrpcWebProtocolConstants.GrpcWebTextContentType, contentType))
            {
                mode = ServerGrpcWebMode.GrpcWebText;
                return true;
            }
        }

        mode = ServerGrpcWebMode.None;
        return false;
    }

    private static partial class Log
    {
       [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "DetectedGrpcWebRequest", Message = "Detected gRPC-Web request from content-type '{ContentType}'.")]
        public static partial void DetectedGrpcWebRequest(ILogger<GrpcWebMiddleware> logger, string contentType);

        [LoggerMessage(Level = LogLevel.Debug, EventId = 2, EventName = "GrpcWebRequestNotProcessed", Message = $"gRPC-Web request not processed. gRPC-Web must be enabled by placing the [EnableGrpcWeb] attribute on a service or method, or enable for all services in the app with {nameof(GrpcWebOptions)}.")]
        public static partial void GrpcWebRequestNotProcessed(ILogger<GrpcWebMiddleware> logger);

        [LoggerMessage(Level = LogLevel.Debug, EventId = 3, EventName = "SendingGrpcWebResponse", Message = "Sending gRPC-Web response with content-type '{ContentType}'.")]
        public static partial void SendingGrpcWebResponse(ILogger<GrpcWebMiddleware> logger, string contentType);
    }
}
