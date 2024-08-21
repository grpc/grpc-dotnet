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

using Grpc.Net.Client.Web.Internal;
using Grpc.Shared;

namespace Grpc.Net.Client.Web;

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
    internal const string WebAssemblyEnableStreamingResponseKey = "WebAssemblyEnableStreamingResponse";

    // Internal and mutable for unit testing.
    internal IOperatingSystem OperatingSystem { get; set; } = Internal.OperatingSystem.Instance;

    /// <summary>
    /// Gets or sets the HTTP version to use when making gRPC-Web calls.
    /// <para>
    /// When a <see cref="Version"/> is specified the value will be set on <see cref="HttpRequestMessage.Version"/>
    /// as gRPC-Web calls are made. Changing this property allows the HTTP version of gRPC-Web calls to
    /// be overridden.
    /// </para>
    /// </summary>
#if NET5_0_OR_GREATER
    [Obsolete("HttpVersion is obsolete and will be removed in a future release. Use GrpcChannelOptions.HttpVersion and GrpcChannelOptions.HttpVersionPolicy instead.")]
#else
    [Obsolete("HttpVersion is obsolete and will be removed in a future release. Use GrpcChannelOptions.HttpVersion instead.")]
#endif
    public Version? HttpVersion { get; set; }

    /// <summary>
    /// Gets or sets the gRPC-Web mode to use when making gRPC-Web calls.
    /// <para>
    /// When <see cref="GrpcWebMode.GrpcWeb"/>, gRPC-Web calls are made with the <c>application/grpc-web</c> content type,
    /// and binary gRPC messages are sent and received.
    /// </para>
    /// <para>
    /// When <see cref="GrpcWebMode.GrpcWebText"/>, gRPC-Web calls are made with the <c>application/grpc-web-text</c> content type,
    /// and base64 encoded gRPC messages are sent and received. Base64 encoding is required for gRPC-Web server streaming calls
    /// to stream correctly in browser apps.
    /// </para>
    /// </summary>
    public GrpcWebMode GrpcWebMode { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="GrpcWebHandler"/>.
    /// </summary>
    public GrpcWebHandler()
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="GrpcWebHandler"/>.
    /// </summary>
    /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
    public GrpcWebHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="GrpcWebHandler"/>.
    /// </summary>
    /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
    public GrpcWebHandler(GrpcWebMode mode)
    {
        GrpcWebMode = mode;
    }

    /// <summary>
    /// Creates a new instance of <see cref="GrpcWebHandler"/>.
    /// </summary>
    /// <param name="mode">The gRPC-Web mode to use when making gRPC-Web calls.</param>
    /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
    public GrpcWebHandler(GrpcWebMode mode, HttpMessageHandler innerHandler) : base(innerHandler)
    {
        GrpcWebMode = mode;
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
        request.Content = new GrpcWebRequestContent(request.Content!, GrpcWebMode);

        // https://github.com/grpc/grpc/blob/f8a5022a2629e0929eb30e0583af66f0c220791b/doc/PROTOCOL-WEB.md
        // The client library should indicate to the server via the "Accept" header that the response stream
        // needs to be text encoded e.g. when XHR is used or due to security policies with XHR.
        if (GrpcWebMode == GrpcWebMode.GrpcWebText)
        {
            request.Headers.TryAddWithoutValidation("Accept", GrpcWebProtocolConstants.GrpcWebTextContentType);
        }

        if (OperatingSystem.IsBrowser)
        {
            FixBrowserUserAgent(request);
        }

        // Set WebAssemblyEnableStreamingResponse to true on gRPC-Web request.
        // https://github.com/mono/mono/blob/a0d69a4e876834412ba676f544d447ec331e7c01/sdks/wasm/framework/src/System.Net.Http.WebAssemblyHttpHandler/WebAssemblyHttpHandler.cs#L149
        //
        // This must be set so WASM will stream the response. Without this setting the WASM HTTP handler will only
        // return content once the entire response has been downloaded. This breaks server streaming.
        //
        // https://github.com/mono/mono/issues/18718
        request.SetOption(WebAssemblyEnableStreamingResponseKey, true);

#pragma warning disable CS0618 // Type or member is obsolete
        if (HttpVersion != null)
        {
            // This doesn't guarantee that the specified version is used. Some handlers will ignore it.
            // For example, version in the browser always negotiated by the browser and HttpClient
            // uses what the browser has negotiated.
            request.Version = HttpVersion;
#if NET5_0_OR_GREATER
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
#endif
        }
#pragma warning restore CS0618 // Type or member is obsolete
#if NET5_0_OR_GREATER
        else if (request.RequestUri?.Scheme == Uri.UriSchemeHttps
            && request.VersionPolicy == HttpVersionPolicy.RequestVersionExact
            && request.Version == System.Net.HttpVersion.Version20)
        {
            // If no explicit HttpVersion is set and the request is using TLS then change the version policy
            // to allow for HTTP/1.1. HttpVersionPolicy.RequestVersionOrLower it will be compatible
            // with HTTP/1.1 and HTTP/2.
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        }
#endif
#if NETSTANDARD2_0
        else if (Http2NotSupported())
        {
            // Platform doesn't support HTTP/2. Default version to HTTP/1.1.
            // This will get set on .NET Framework.
            request.Version = System.Net.HttpVersion.Version11;
        }
#endif

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Content != null && IsMatchingResponseContentType(GrpcWebMode, response.Content.Headers.ContentType?.MediaType))
        {
#if NETSTANDARD2_0
            // In netstandard2.0 we look for headers in request properties. Need to create them.
            response.EnsureTrailingHeaders();
#endif

            response.Content = new GrpcWebResponseContent(response.Content, GrpcWebMode, response.TrailingHeaders());
        }

        // The gRPC client validates HTTP version 2.0 and will error if it isn't. Always set
        // the version to 2.0, even for non-gRPC content type. The client maps HTTP status codes
        // to gRPC statuses, e.g. HTTP 404 -> gRPC unimplemented.
        //
        // Note: Some handlers don't correctly set HttpResponseMessage.Version.
        // We can't rely on it being set correctly. It is safest to always set it to 2.0.
        response.Version = GrpcWebProtocolConstants.Http2Version;

        return response;
    }

    private bool Http2NotSupported()
    {
        if (Environment.Version.Major == 4 &&
            Environment.Version.Minor == 0 &&
            Environment.Version.Build == 30319 &&
            InnerHandler != null &&
            HttpRequestHelpers.HasHttpHandlerType<HttpClientHandler>(InnerHandler))
        {
            // https://docs.microsoft.com/dotnet/api/system.environment.version#remarks
            // Detect runtimes between .NET 4.5 and .NET Core 2.1
            // The default HttpClientHandler doesn't support HTTP/2.
            return true;
        }

        return false;
    }

    private void FixBrowserUserAgent(HttpRequestMessage request)
    {
        const string userAgentHeader = "User-Agent";

        // Remove the user-agent header and re-add it as x-user-agent.
        // We don't want to override the browser's user-agent value.
        // Consistent with grpc-web JS client which sends its header in x-user-agent.
        // https://github.com/grpc/grpc-web/blob/2e3e8d2c501c4ddce5406ac24a637003eabae4cf/javascript/net/grpc/web/grpcwebclientbase.js#L323
        if (request.Headers.TryGetValues(userAgentHeader, out var values))
        {
            request.Headers.Remove(userAgentHeader);
            request.Headers.TryAddWithoutValidation("X-User-Agent", values);
        }
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
