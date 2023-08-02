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

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Internal;

internal abstract class GrpcCall
{
    // Getting logger name from generic type is slow
    private const string LoggerName = "Grpc.Net.Client.Internal.GrpcCall";

    private GrpcCallSerializationContext? _serializationContext;
    private DefaultDeserializationContext? _deserializationContext;

    protected Metadata? Trailers { get; set; }

    public bool ResponseFinished { get; protected set; }
    public HttpResponseMessage? HttpResponse { get; protected set; }

    public GrpcCallSerializationContext SerializationContext
    {
        get { return _serializationContext ??= new GrpcCallSerializationContext(this); }
    }

    public DefaultDeserializationContext DeserializationContext
    {
        get { return _deserializationContext ??= new DefaultDeserializationContext(); }
    }

    public CallOptions Options { get; }
    public ILogger Logger { get; }
    public GrpcChannel Channel { get; }

    public string? RequestGrpcEncoding { get; internal set; }

    public abstract Task<Status> CallTask { get; }
    public abstract CancellationToken CancellationToken { get; }
    public abstract Type RequestType { get; }
    public abstract Type ResponseType { get; }

    protected GrpcCall(CallOptions options, GrpcChannel channel)
    {
        Options = options;
        Channel = channel;
        Logger = channel.LoggerFactory.CreateLogger(LoggerName);
    }

    public Exception CreateCanceledStatusException(Exception? ex = null)
    {
        var status = (CallTask.IsCompletedSuccessfully()) ? CallTask.Result : new Status(StatusCode.Cancelled, string.Empty, ex);
        return CreateRpcException(status);
    }

    public CancellationToken GetCanceledToken(CancellationToken methodCancellationToken)
    {
        if (methodCancellationToken.IsCancellationRequested)
        {
            return methodCancellationToken;
        }
        else if (Options.CancellationToken.IsCancellationRequested)
        {
            return Options.CancellationToken;
        }
        else if (CancellationToken.IsCancellationRequested)
        {
            return CancellationToken;
        }
        return CancellationToken.None;
    }

    internal RpcException CreateRpcException(Status status)
    {
        // This code can be called from a background task.
        // If an error is thrown when parsing the trailers into a Metadata
        // collection then the background task will never complete and
        // the gRPC call will hang. If the trailers are invalid then log
        // an error message and return an empty trailers collection
        // on the RpcException that we want to return to the app.
        Metadata? trailers = null;
        try
        {
            TryGetTrailers(out trailers);
        }
        catch (Exception ex)
        {
            GrpcCallLog.ErrorParsingTrailers(Logger, ex);
        }
        return new RpcException(status, trailers ?? Metadata.Empty);
    }

    public Exception CreateFailureStatusException(Status status)
    {
        if (Channel.ThrowOperationCanceledOnCancellation &&
            (status.StatusCode == StatusCode.DeadlineExceeded || status.StatusCode == StatusCode.Cancelled))
        {
            // Convert status response of DeadlineExceeded to OperationCanceledException when
            // ThrowOperationCanceledOnCancellation is true.
            // This avoids a race between the client-side timer and the server status throwing different
            // errors on deadline exceeded.
            return new OperationCanceledException();
        }
        else
        {
            return CreateRpcException(status);
        }
    }

    protected bool TryGetTrailers([NotNullWhen(true)] out Metadata? trailers)
    {
        if (Trailers == null)
        {
            // Trailers are read from the end of the request.
            // If the request isn't finished then we can't get the trailers.
            if (!ResponseFinished)
            {
                trailers = null;
                return false;
            }

            CompatibilityHelpers.Assert(HttpResponse != null);
            Trailers = GrpcProtocolHelpers.BuildMetadata(HttpResponse.TrailingHeaders());
        }

        trailers = Trailers;
        return true;
    }

    internal static Status? ValidateHeaders(HttpResponseMessage httpResponse, out Metadata? trailers)
    {
        // gRPC status can be returned in the header when there is no message (e.g. unimplemented status)
        // An explicitly specified status header has priority over other failing statuses
        if (GrpcProtocolHelpers.TryGetStatusCore(httpResponse.Headers, out var status))
        {
            // Trailers are in the header because there is no message.
            // Note that some default headers will end up in the trailers (e.g. Date, Server).
            trailers = GrpcProtocolHelpers.BuildMetadata(httpResponse.Headers);
            return status;
        }

        trailers = null;

        // ALPN negotiation is sending HTTP/1.1 and HTTP/2.
        // Check that the response wasn't downgraded to HTTP/1.1.
        if (httpResponse.Version < GrpcProtocolConstants.Http2Version)
        {
            return new Status(StatusCode.Internal, $"Bad gRPC response. Response protocol downgraded to HTTP/{httpResponse.Version.ToString(2)}.");
        }

        if (httpResponse.StatusCode != HttpStatusCode.OK)
        {
            var statusCode = MapHttpStatusToGrpcCode(httpResponse.StatusCode);
            return new Status(statusCode, "Bad gRPC response. HTTP status code: " + (int)httpResponse.StatusCode);
        }

        // Don't access Headers.ContentType property because it is not threadsafe.
        var contentType = GrpcProtocolHelpers.GetHeaderValue(httpResponse.Content?.Headers, "Content-Type");
        if (contentType == null)
        {
            return new Status(StatusCode.Cancelled, "Bad gRPC response. Response did not have a content-type header.");
        }

        if (!CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, contentType))
        {
            return new Status(StatusCode.Cancelled, "Bad gRPC response. Invalid content-type value: " + contentType);
        }

        // Call is still in progress
        return null;
    }

    private static StatusCode MapHttpStatusToGrpcCode(HttpStatusCode httpStatusCode)
    {
        switch (httpStatusCode)
        {
            case HttpStatusCode.BadRequest:  // 400
#if !NETSTANDARD2_0 && !NET462
            case HttpStatusCode.RequestHeaderFieldsTooLarge: // 431
#else
            case (HttpStatusCode)431:
#endif
                return StatusCode.Internal;
            case HttpStatusCode.Unauthorized:  // 401
                return StatusCode.Unauthenticated;
            case HttpStatusCode.Forbidden:  // 403
                return StatusCode.PermissionDenied;
            case HttpStatusCode.NotFound:  // 404
                return StatusCode.Unimplemented;
#if !NETSTANDARD2_0 && !NET462
            case HttpStatusCode.TooManyRequests:  // 429
#else
            case (HttpStatusCode)429:
#endif
            case HttpStatusCode.BadGateway:  // 502
            case HttpStatusCode.ServiceUnavailable:  // 503
            case HttpStatusCode.GatewayTimeout:  // 504
                return StatusCode.Unavailable;
            default:
                if ((int)httpStatusCode >= 100 && (int)httpStatusCode < 200)
                {
                    // 1xx. These headers should have been ignored.
                    return StatusCode.Internal;
                }

                return StatusCode.Unknown;
        }
    }

    protected internal sealed class ActivityStartData
    {
#if NET5_0_OR_GREATER
        // Common properties. Properties not in this list could be trimmed.
        [DynamicDependency(nameof(HttpRequestMessage.RequestUri), typeof(HttpRequestMessage))]
        [DynamicDependency(nameof(HttpRequestMessage.Method), typeof(HttpRequestMessage))]
        [DynamicDependency(nameof(Uri.Host), typeof(Uri))]
        [DynamicDependency(nameof(Uri.Port), typeof(Uri))]
#endif
        internal ActivityStartData(HttpRequestMessage request)
        {
            Request = request;
        }

        public HttpRequestMessage Request { get; }

        public override string ToString() => $"{{ {nameof(Request)} = {Request} }}";
    }

    protected internal sealed class ActivityStopData
    {
#if NET5_0_OR_GREATER
        // Common properties. Properties not in this list could be trimmed.
        [DynamicDependency(nameof(HttpRequestMessage.RequestUri), typeof(HttpRequestMessage))]
        [DynamicDependency(nameof(HttpRequestMessage.Method), typeof(HttpRequestMessage))]
        [DynamicDependency(nameof(Uri.Host), typeof(Uri))]
        [DynamicDependency(nameof(Uri.Port), typeof(Uri))]
        [DynamicDependency(nameof(HttpResponseMessage.StatusCode), typeof(HttpResponseMessage))]
#endif
        internal ActivityStopData(HttpResponseMessage? response, HttpRequestMessage request)
        {
            Response = response;
            Request = request;
        }

        public HttpResponseMessage? Response { get; }
        public HttpRequestMessage Request { get; }

        public override string ToString() => $"{{ {nameof(Response)} = {Response}, {nameof(Request)} = {Request} }}";
    }
}
