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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.NetCore.HttpClient.Internal
{
    internal class GrpcCall<TRequest, TResponse>
    {
        private readonly CancellationTokenSource _callCts;
        private readonly CancellationTokenRegistration? _ctsRegistration;
        private readonly ISystemClock _clock;
        private readonly TimeSpan? _timeout;
        private readonly Timer _deadlineTimer;
        private Metadata _trailers;
        private CancellationTokenRegistration? _writerCtsRegistration;
        private string _headerValidationError;

        public bool DeadlineReached { get; private set; }
        public bool Disposed { get; private set; }
        public bool ResponseFinished { get; private set; }
        public HttpResponseMessage HttpResponse { get; private set; }
        public CallOptions Options { get; }
        public Method<TRequest, TResponse> Method { get; }
        public Task SendTask { get; private set; }
        public HttpContentClientStreamWriter<TRequest, TResponse> ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse> ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, CallOptions options, ISystemClock clock)
        {
            _callCts = new CancellationTokenSource();
            Method = method;
            Options = options;
            _clock = clock;

            if (options.CancellationToken.CanBeCanceled)
            {
                // The cancellation token will cancel the call CTS
                _ctsRegistration = options.CancellationToken.Register(CancelCall);
            }

            if (options.Deadline != null && options.Deadline != DateTime.MaxValue)
            {
                var timeout = options.Deadline.Value - _clock.UtcNow;
                _timeout = (timeout > TimeSpan.Zero) ? timeout : TimeSpan.Zero;
            }

            if (_timeout != null)
            {
                // Deadline timer will cancel the call CTS
                _deadlineTimer = new Timer(DeadlineExceeded, null, _timeout.Value, Timeout.InfiniteTimeSpan);
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

        public void StartUnary(System.Net.Http.HttpClient client, TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            StartSend(client, message);
        }

        public void StartClientStreaming(System.Net.Http.HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            StartSend(client, message);
        }

        public void StartServerStreaming(System.Net.Http.HttpClient client, TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            StartSend(client, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void StartDuplexStreaming(System.Net.Http.HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            StartSend(client, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                Disposed = true;

                _callCts.Cancel();
                _ctsRegistration?.Dispose();
                _writerCtsRegistration?.Dispose();
                _deadlineTimer?.Dispose();
                HttpResponse?.Dispose();
                ClientStreamReader?.Dispose();
                ClientStreamWriter?.Dispose();

                _callCts.Dispose();
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

        public void FinishResponse()
        {
            if (ResponseFinished)
            {
                return;
            }

            ResponseFinished = true;

            // Clean up call resources once this call is finished
            // Call may not be explicitly disposed when used with unary methods
            // e.g. var reply = await client.SayHelloAsync(new HelloRequest());
            Dispose();

            var status = GetStatusCore(HttpResponse);
            if (status.StatusCode != StatusCode.OK)
            {
                throw new RpcException(status);
            }
        }

        public async Task<Metadata> GetResponseHeadersAsync()
        {
            try
            {
                await SendTask.ConfigureAwait(false);

                // The task of this method is cached so there is no need to cache the headers here
                return GrpcProtocolHelpers.BuildMetadata(HttpResponse.Headers);
            }
            catch (OperationCanceledException)
            {
                EnsureNotDisposed();
                throw CreateCanceledStatusException();
            }
        }

        public Status GetStatus()
        {
            ValidateTrailersAvailable();

            return GetStatusCore(HttpResponse);
        }

        public async Task<TResponse> GetResponseAsync()
        {
            try
            {
                await SendTask.ConfigureAwait(false);

                // Trailers are only available once the response body had been read
                var responseStream = await HttpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var message = await responseStream.ReadSingleMessageAsync(Method.ResponseMarshaller.Deserializer, _callCts.Token).ConfigureAwait(false);
                FinishResponse();

                if (message == null)
                {
                    throw new InvalidOperationException("Call did not return a response message");
                }

                // The task of this method is cached so there is no need to cache the message here
                return message;
            }
            catch (OperationCanceledException)
            {
                EnsureNotDisposed();
                throw CreateCanceledStatusException();
            }
        }

        private void ValidateHeaders()
        {
            if (HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                _headerValidationError = "Bad gRPC response. Expected HTTP status code 200. Got status code: " + (int)HttpResponse.StatusCode;
            }
            else if (HttpResponse.Content.Headers.ContentType == null)
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
                Dispose();

                throw new InvalidOperationException(_headerValidationError);
            }

            // Success!
        }

        public Metadata GetTrailers()
        {
            if (_trailers == null)
            {
                ValidateTrailersAvailable();

                _trailers = GrpcProtocolHelpers.BuildMetadata(HttpResponse.TrailingHeaders);
            }

            return _trailers;
        }

        private void SetMessageContent(TRequest request, HttpRequestMessage message)
        {
            message.Content = new PushStreamContent(
                (stream) =>
                {
                    return SerializationHelpers.WriteMessage<TRequest>(stream, request, Method.RequestMarshaller.Serializer, Options.CancellationToken);
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        private void CancelCall()
        {
            _callCts.Cancel();
        }

        private void StartSend(System.Net.Http.HttpClient client, HttpRequestMessage message)
        {
            SendTask = SendAsync(client, message);
        }

        private async Task SendAsync(System.Net.Http.HttpClient client, HttpRequestMessage message)
        {
            HttpResponse = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, _callCts.Token).ConfigureAwait(false);
            ValidateHeaders();
        }

        private HttpContentClientStreamWriter<TRequest, TResponse> CreateWriter(HttpRequestMessage message)
        {
            var writeStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Canceling call will cancel pending writes to the stream
            _writerCtsRegistration = _callCts.Token.Register(() =>
            {
                completeTcs.TrySetCanceled();
                writeStreamTcs.TrySetCanceled();
            });

            message.Content = new PushStreamContent(
                (stream) =>
                {
                    writeStreamTcs.TrySetResult(stream);
                    return completeTcs.Task;
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);

            var writer = new HttpContentClientStreamWriter<TRequest, TResponse>(this, writeStreamTcs.Task, completeTcs);
            return writer;
        }

        private HttpRequestMessage CreateHttpRequestMessage()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, Method.FullName);
            message.Version = new Version(2, 0);

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
            if (!_callCts.IsCancellationRequested)
            {
                // Flag is used to determine status code when generating exceptions
                DeadlineReached = true;

                _callCts.Cancel();
            }
        }

        private static Status GetStatusCore(HttpResponseMessage httpResponseMessage)
        {
            // grpc-status is a required trailer
            string grpcStatus;
            if (!httpResponseMessage.TrailingHeaders.TryGetValues(GrpcProtocolConstants.StatusTrailer, out var grpcStatusValues) ||
                (grpcStatus = grpcStatusValues.FirstOrDefault()) == null)
            {
                throw new InvalidOperationException("Response did not have a grpc-status trailer.");
            }

            int statusValue;
            if (!int.TryParse(grpcStatus, out statusValue))
            {
                throw new InvalidOperationException("Unexpected grpc-status value: " + grpcStatus);
            }

            // grpc-message is optional
            string grpcMessage = null;
            if (httpResponseMessage.TrailingHeaders.TryGetValues(GrpcProtocolConstants.MessageTrailer, out var grpcMessageValues))
            {
                // TODO(JamesNK): Unescape percent encoding
                grpcMessage = grpcMessageValues.FirstOrDefault();
            }

            return new Status((StatusCode)statusValue, grpcMessage);
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
            if (SendTask.IsFaulted)
            {
                throw new InvalidOperationException("Can't get the call trailers because an error occured when making the request.", SendTask.Exception);
            }

            throw new InvalidOperationException("Can't get the call trailers because the call is not complete.");
        }
    }
}
