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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Internal
{
    internal partial class GrpcCall<TRequest, TResponse> : IDisposable
        where TRequest : class
        where TResponse : class
    {
        private readonly CancellationTokenSource _callCts;
        private readonly CancellationTokenRegistration? _ctsRegistration;
        private readonly ISystemClock _clock;
        private readonly TimeSpan? _timeout;
        private readonly Uri _uri;
        private readonly GrpcCallScope _logScope;

        private Timer? _deadlineTimer;
        private Metadata? _trailers;
        private string? _headerValidationError;
        private TaskCompletionSource<Stream>? _writeStreamTcs;
        private TaskCompletionSource<bool>? _completeTcs;

        public bool DeadlineReached { get; private set; }
        public bool Disposed { get; private set; }
        public bool ResponseFinished { get; private set; }
        public HttpResponseMessage? HttpResponse { get; private set; }
        public CallOptions Options { get; }
        public Method<TRequest, TResponse> Method { get; }

        public ILogger Logger { get; }
        public Task? SendTask { get; private set; }
        public HttpContentClientStreamWriter<TRequest, TResponse>? ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse>? ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, CallOptions options, ISystemClock clock, ILoggerFactory loggerFactory)
        {
            // Validate deadline before creating any objects that require cleanup
            ValidateDeadline(options.Deadline);

            _callCts = new CancellationTokenSource();
            Method = method;
            _uri = new Uri(method.FullName, UriKind.Relative);
            _logScope = new GrpcCallScope(method.Type, _uri);
            Options = options;
            _clock = clock;
            Logger = loggerFactory.CreateLogger<GrpcCall<TRequest, TResponse>>();

            if (options.CancellationToken.CanBeCanceled)
            {
                // The cancellation token will cancel the call CTS
                _ctsRegistration = options.CancellationToken.Register(() =>
                {
                    using (StartScope())
                    {
                        CancelCall();
                    }
                });
            }

            if (options.Deadline != null && options.Deadline != DateTime.MaxValue)
            {
                var timeout = options.Deadline.GetValueOrDefault() - _clock.UtcNow;
                _timeout = (timeout > TimeSpan.Zero) ? timeout : TimeSpan.Zero;
            }
        }

        private void ValidateDeadline(DateTime? deadline)
        {
            if (deadline != null && deadline != DateTime.MaxValue && deadline != DateTime.MinValue && deadline.Value.Kind != DateTimeKind.Utc)
            {
                throw new InvalidOperationException("Deadline must have a kind DateTimeKind.Utc or be equal to DateTime.MaxValue or DateTime.MinValue.");
            }
        }

        public CancellationToken CancellationToken
        {
            get { return _callCts.Token; }
        }

        public bool IsCancellationRequested
        {
            get { return _callCts.IsCancellationRequested; }
        }

        public void StartUnary(HttpClient client, TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            StartSend(client, message);
        }

        public void StartClientStreaming(HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            StartSend(client, message);
        }

        public void StartServerStreaming(HttpClient client, TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            StartSend(client, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void StartDuplexStreaming(HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            StartSend(client, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        /// <summary>
        /// Dispose can be called by:
        /// 1. The user. AsyncUnaryCall.Dispose et al will call this Dispose
        /// 2. <see cref="ValidateHeaders"/> will call dispose if errors fail validation
        /// 3. <see cref="FinishResponse"/> will call dispose
        /// </summary>
        public void Dispose()
        {
            using (StartScope())
            {
                DisposeCore();
            }
        }

        private void DisposeCore()
        {
            if (!Disposed)
            {
                Disposed = true;

                if (!ResponseFinished)
                {
                    // If the response is not finished then cancel any pending actions:
                    // 1. Call HttpClient.SendAsync
                    // 2. Response Stream.ReadAsync
                    // 3. Client stream
                    //    - Getting the Stream from the Request.HttpContent
                    //    - Holding the Request.HttpContent.SerializeToStream open
                    //    - Writing to the client stream
                    CancelCall();
                }
                else
                {
                    _writeStreamTcs?.TrySetCanceled();
                    _completeTcs?.TrySetCanceled();
                }

                _ctsRegistration?.Dispose();
                _deadlineTimer?.Dispose();
                HttpResponse?.Dispose();
                ClientStreamReader?.Dispose();
                ClientStreamWriter?.Dispose();

                // To avoid racing with Dispose, skip disposing the call CTS
                // This avoid Dispose potentially calling cancel on a disposed CTS
                // The call CTS is not exposed externally and all dependent registrations
                // are cleaned up
            }
        }

        public void EnsureNotDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcCall<TRequest, TResponse>));
            }
        }

        public void EnsureHeadersValid()
        {
            if (_headerValidationError != null)
            {
                throw new InvalidOperationException(_headerValidationError);
            }
        }

        public Exception CreateCanceledStatusException()
        {
            if (_headerValidationError != null)
            {
                return new InvalidOperationException(_headerValidationError);
            }

            var statusCode = DeadlineReached ? StatusCode.DeadlineExceeded : StatusCode.Cancelled;
            return new RpcException(new Status(statusCode, string.Empty));
        }

        /// <summary>
        /// Marks the response as finished, i.e. all response content has been read and trailers are available.
        /// Can be called by <see cref="GetResponseAsync"/> for unary and client streaming calls, or
        /// <see cref="HttpContentClientStreamReader{TRequest,TResponse}.MoveNextCore(CancellationToken)"/>
        /// for server streaming and duplex streaming calls.
        /// </summary>
        public void FinishResponse()
        {
            ResponseFinished = true;
            Debug.Assert(HttpResponse != null);

            try
            {
                // Get status from response before dispose
                // This may throw an error if the grpc-status is missing or malformed
                var status = GetStatusCore(HttpResponse);

                if (status.StatusCode != StatusCode.OK)
                {
                    Log.GrpcStatusError(Logger, status.StatusCode, status.Detail);
                    throw new RpcException(status);
                }
            }
            finally
            {
                Log.FinishedCall(Logger);

                // Clean up call resources once this call is finished
                // Call may not be explicitly disposed when used with unary methods
                // e.g. var reply = await client.SayHelloAsync(new HelloRequest());
                DisposeCore();
            }
        }

        public async Task<Metadata> GetResponseHeadersAsync()
        {
            Debug.Assert(SendTask != null);

            try
            {
                using (StartScope())
                {
                    await SendTask.ConfigureAwait(false);
                    Debug.Assert(HttpResponse != null);

                    // The task of this method is cached so there is no need to cache the headers here
                    return GrpcProtocolHelpers.BuildMetadata(HttpResponse.Headers);
                }
            }
            catch (OperationCanceledException)
            {
                EnsureNotDisposed();
                throw CreateCanceledStatusException();
            }
        }

        public Status GetStatus()
        {
            Debug.Assert(HttpResponse != null);

            using (StartScope())
            {
                ValidateTrailersAvailable();

                return GetStatusCore(HttpResponse);
            }
        }

        public async Task<TResponse> GetResponseAsync()
        {
            Debug.Assert(SendTask != null);

            try
            {
                using (StartScope())
                {
                    await SendTask.ConfigureAwait(false);
                    Debug.Assert(HttpResponse != null);

                    // Trailers are only available once the response body had been read
                    var responseStream = await HttpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var message = await responseStream.ReadSingleMessageAsync(Logger, Method.ResponseMarshaller.Deserializer, _callCts.Token).ConfigureAwait(false);
                    FinishResponse();

                    if (message == null)
                    {
                        Log.MessageNotReturned(Logger);
                        throw new InvalidOperationException("Call did not return a response message");
                    }

                    // The task of this method is cached so there is no need to cache the message here
                    return message;
                }
            }
            catch (OperationCanceledException)
            {
                EnsureNotDisposed();
                throw CreateCanceledStatusException();
            }
        }

        private void ValidateHeaders()
        {
            Log.ResponseHeadersReceived(Logger);

            Debug.Assert(HttpResponse != null);
            if (HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                _headerValidationError = "Bad gRPC response. Expected HTTP status code 200. Got status code: " + (int)HttpResponse.StatusCode;
            }
            else if (HttpResponse.Content?.Headers.ContentType == null)
            {
                _headerValidationError = "Bad gRPC response. Response did not have a content-type header.";
            }
            else
            {
                var grpcEncoding = HttpResponse.Content.Headers.ContentType.ToString();
                if (!GrpcProtocolHelpers.IsGrpcContentType(grpcEncoding))
                {
                    _headerValidationError = "Bad gRPC response. Invalid content-type value: " + grpcEncoding;
                }
            }

            if (_headerValidationError != null)
            {
                // Response is not valid gRPC
                // Clean up/cancel any pending operations
                DisposeCore();

                throw new InvalidOperationException(_headerValidationError);
            }

            // Success!
        }

        public Metadata GetTrailers()
        {
            using (StartScope())
            {
                if (_trailers == null)
                {
                    ValidateTrailersAvailable();

                    Debug.Assert(HttpResponse != null);
                    _trailers = GrpcProtocolHelpers.BuildMetadata(HttpResponse.TrailingHeaders);
                }

                return _trailers;
            }
        }

        private void SetMessageContent(TRequest request, HttpRequestMessage message)
        {
            message.Content = new PushStreamContent(
                (stream) =>
                {
                    return stream.WriteMessage<TRequest>(Logger, request, Method.RequestMarshaller.Serializer, Options.CancellationToken);
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        private void CancelCall()
        {
            // Checking if cancellation has already happened isn't threadsafe
            // but there is no adverse effect other than an extra log message
            if (!_callCts.IsCancellationRequested)
            {
                Log.CanceledCall(Logger);

                _callCts.Cancel();

                // Canceling call will cancel pending writes to the stream
                _completeTcs?.TrySetCanceled();
                _writeStreamTcs?.TrySetCanceled();
            }
        }

        internal IDisposable? StartScope()
        {
            // Only return a scope if the logger is enabled to log 
            // in at least Critical level for performance
            if (Logger.IsEnabled(LogLevel.Critical))
            {
                return Logger.BeginScope(_logScope);
            }

            return null;
        }

        private void StartSend(HttpClient client, HttpRequestMessage message)
        {
            using (StartScope())
            {
                if (_timeout != null)
                {
                    Log.StartingDeadlineTimeout(Logger, _timeout.Value);

                    // Deadline timer will cancel the call CTS
                    // Start timer after reader/writer have been created, otherwise a zero length deadline could cancel
                    // the call CTS before they are created and leave them in a non-canceled state
                    _deadlineTimer = new Timer(DeadlineExceeded, null, _timeout.Value, Timeout.InfiniteTimeSpan);
                }

                SendTask = SendAsync(client, message);
            }
        }

        private async Task SendAsync(HttpClient client, HttpRequestMessage message)
        {
            Log.StartingCall(Logger, Method.Type, message.RequestUri);

            try
            {
                HttpResponse = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, _callCts.Token).ConfigureAwait(false);

                _callCts.Token.Register(() =>
                {
                    string s = string.Empty;
                });
            }
            catch (Exception ex)
            {
                Log.ErrorStartingCall(Logger, ex);
                throw;
            }

            ValidateHeaders();
        }

        private HttpContentClientStreamWriter<TRequest, TResponse> CreateWriter(HttpRequestMessage message)
        {
            _writeStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            message.Content = new PushStreamContent(
                (stream) =>
                {
                    _writeStreamTcs.TrySetResult(stream);
                    return _completeTcs.Task;
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);

            var writer = new HttpContentClientStreamWriter<TRequest, TResponse>(this, _writeStreamTcs.Task, _completeTcs);
            return writer;
        }

        private HttpRequestMessage CreateHttpRequestMessage()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _uri);
            message.Version = new Version(2, 0);
            // User agent is optional but recommended
            message.Headers.UserAgent.Add(GrpcProtocolConstants.UserAgentHeader);
            // TE is required by some servers, e.g. C Core
            // A missing TE header results in servers aborting the gRPC call
            message.Headers.TE.Add(GrpcProtocolConstants.TEHeader);

            if (Options.Headers != null && Options.Headers.Count > 0)
            {
                foreach (var entry in Options.Headers)
                {
                    // Deadline is set via CallOptions.Deadline
                    if (entry.Key == GrpcProtocolConstants.TimeoutHeader)
                    {
                        continue;
                    }

                    var value = entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value;
                    message.Headers.Add(entry.Key, value);
                }
            }

            if (_timeout != null)
            {
                message.Headers.Add(GrpcProtocolConstants.TimeoutHeader, GrpcProtocolHelpers.EncodeTimeout(Convert.ToInt64(_timeout.Value.TotalMilliseconds)));
            }

            return message;
        }

        private void DeadlineExceeded(object state)
        {
            // Deadline is only exceeded if the timeout has passed and
            // the response has not been finished or canceled
            if (!_callCts.IsCancellationRequested && !ResponseFinished)
            {
                Log.DeadlineExceeded(Logger);

                // Flag is used to determine status code when generating exceptions
                DeadlineReached = true;

                CancelCall();
            }
        }

        private static Status GetStatusCore(HttpResponseMessage httpResponseMessage)
        {
            // A gRPC server may return gRPC status in the headers when the response stream is empty
            // For example, C Core server returns them together in the empty_stream interop test
            HttpResponseHeaders statusHeaders;

            var grpcStatus = GetHeaderValue(httpResponseMessage.TrailingHeaders, GrpcProtocolConstants.StatusTrailer);
            if (grpcStatus != null)
            {
                statusHeaders = httpResponseMessage.TrailingHeaders;
            }
            else
            {
                grpcStatus = GetHeaderValue(httpResponseMessage.Headers, GrpcProtocolConstants.StatusTrailer);

                // grpc-status is a required trailer
                if (grpcStatus != null)
                {
                    statusHeaders = httpResponseMessage.Headers;
                }
                else
                {
                    throw new InvalidOperationException("Response did not have a grpc-status trailer.");
                }
            }

            int statusValue;
            if (!int.TryParse(grpcStatus, out statusValue))
            {
                throw new InvalidOperationException("Unexpected grpc-status value: " + grpcStatus);
            }

            // grpc-message is optional
            // Always read the gRPC message from the same headers collection as the status
            var grpcMessage = GetHeaderValue(statusHeaders, GrpcProtocolConstants.MessageTrailer);

            if (!string.IsNullOrEmpty(grpcMessage))
            {
                // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
                // The value portion of Status-Message is conceptually a Unicode string description of the error,
                // physically encoded as UTF-8 followed by percent-encoding.
                grpcMessage = Uri.UnescapeDataString(grpcMessage);
            }

            return new Status((StatusCode)statusValue, grpcMessage);
        }

        private static string? GetHeaderValue(HttpHeaders headers, string name)
        {
            if (!headers.TryGetValues(name, out var values))
            {
                return null;
            }

            // HttpHeaders appears to always return an array, but fallback to converting values to one just in case
            var valuesArray = values as string[] ?? values.ToArray();

            switch (valuesArray.Length)
            {
                case 0:
                    return null;
                case 1:
                    return valuesArray[0];
                default:
                    throw new InvalidOperationException($"Multiple {name} headers.");
            }
        }

        private void ValidateTrailersAvailable()
        {
            // Response headers have been returned and are not a valid grpc response
            EnsureHeadersValid();

            // Response is finished
            if (ResponseFinished)
            {
                return;
            }

            // Async call could have been disposed
            EnsureNotDisposed();

            // Call could have been canceled or deadline exceeded
            if (_callCts.IsCancellationRequested)
            {
                throw CreateCanceledStatusException();
            }

            // HttpClient.SendAsync could have failed
            Debug.Assert(SendTask != null);
            if (SendTask.IsFaulted)
            {
                throw new InvalidOperationException("Can't get the call trailers because an error occured when making the request.", SendTask.Exception);
            }

            throw new InvalidOperationException("Can't get the call trailers because the call is not complete.");
        }
    }
}
