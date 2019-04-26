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
using System.Net.Http;
using System.Net.Http.Headers;
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

        private HttpResponseMessage _httpResponse;
        private Metadata _trailers;
        private CancellationTokenRegistration? _writerCtsRegistration;

        public bool DeadlineReached { get; private set; }
        public bool Disposed { get; private set; }
        public bool ResponseFinished { get; private set; }
        public CallOptions Options { get; }
        public Method<TRequest, TResponse> Method { get; }
        public Task<HttpResponseMessage> SendTask { get; private set; }
        public HttpContentClientStreamWriter<TRequest, TResponse> ClientStreamWriter { get; private set; }

        public HttpContextClientStreamReader<TRequest, TResponse> StreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, CallOptions options, ISystemClock clock)
        {
            _callCts = new CancellationTokenSource();
            Method = method;
            Options = options;
            _clock = clock;

            if (options.CancellationToken.CanBeCanceled)
            {
                _ctsRegistration = options.CancellationToken.Register(CancelCall);
            }

            if (options.Deadline != null && options.Deadline != DateTime.MaxValue)
            {
                var timeout = options.Deadline.Value - _clock.UtcNow;
                _timeout = (timeout > TimeSpan.Zero) ? timeout : TimeSpan.Zero;
            }

            if (_timeout != null)
            {
                _deadlineTimer = new Timer(ReachDeadline, null, _timeout.Value, Timeout.InfiniteTimeSpan);
            }
        }

        private void ReachDeadline(object state)
        {
            if (!_callCts.IsCancellationRequested)
            {
                DeadlineReached = true;
                _callCts.Cancel();
            }
        }

        private void CancelCall()
        {
            _callCts.Cancel();
        }

        public CancellationToken CancellationToken
        {
            get { return _callCts.Token; }
        }

        public void SendUnary(System.Net.Http.HttpClient client, TRequest request)
        {
            HttpRequestMessage message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            SendCore(client, message);
        }

        private void SetMessageContent(TRequest request, HttpRequestMessage message)
        {
            message.Content = new PushStreamContent(
                (stream) =>
                {
                    return SerialiationHelpers.WriteMessage<TRequest>(stream, request, Method.RequestMarshaller.Serializer, Options.CancellationToken);
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        public void SendClientStreaming(System.Net.Http.HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);

            SendCore(client, message);
        }

        public void SendServerStreaming(System.Net.Http.HttpClient client, TRequest request)
        {
            HttpRequestMessage message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            SendCore(client, message);

            StreamReader = new HttpContextClientStreamReader<TRequest, TResponse>(this);
        }

        public void SendDuplexStreaming(System.Net.Http.HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);

            SendCore(client, message);

            StreamReader = new HttpContextClientStreamReader<TRequest, TResponse>(this);
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                Disposed = true;

                _callCts.Cancel();
                _callCts.Dispose();
                _ctsRegistration?.Dispose();
                _writerCtsRegistration?.Dispose();
                _deadlineTimer?.Dispose();
                _httpResponse?.Dispose();
                StreamReader?.Dispose();
                ClientStreamWriter?.Dispose();
            }
        }

        private void SendCore(System.Net.Http.HttpClient client, HttpRequestMessage message)
        {
            SendTask = client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, _callCts.Token);
        }

        private HttpContentClientStreamWriter<TRequest, TResponse> CreateWriter(HttpRequestMessage message)
        {
            TaskCompletionSource<Stream> writeStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> completeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _writerCtsRegistration = _callCts.Token.Register(() =>
            {
                completeTcs.TrySetCanceled();
                writeStreamTcs.TrySetCanceled();
            });

            message.Content = new PushStreamContent(
                (stream) =>
                {
                    writeStreamTcs.SetResult(stream);
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
                // JamesNK(todo) - Replicate C core's logic for formatting grpc-timeout
                message.Headers.Add(GrpcProtocolConstants.TimeoutHeader, Convert.ToInt64(_timeout.Value.TotalMilliseconds) + "m");
            }

            return message;
        }

        public void EnsureNotDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcCall<TRequest, TResponse>));
            }
        }

        public async Task<TResponse> GetResponseAsync()
        {
            try
            {
                _httpResponse = await SendTask.ConfigureAwait(false);

                // Server might have returned a status without any response body. For example, an unimplemented status
                // Check for the trailer status before attempting to read the body and failing
                if (_httpResponse.TrailingHeaders.Contains(GrpcProtocolConstants.StatusTrailer))
                {
                    FinishResponse(_httpResponse);
                }

                var responseStream = await _httpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var message = await responseStream.ReadSingleMessageAsync(Method.ResponseMarshaller.Deserializer, _callCts.Token).ConfigureAwait(false);
                FinishResponse(_httpResponse);

                // The task of this method is cached so there is no need to cache the message here
                return message;
            }
            catch (TaskCanceledException)
            {
                throw CreateCanceledStatusException();
            }
            catch (OperationCanceledException)
            {
                throw CreateCanceledStatusException();
            }
        }

        internal RpcException CreateCanceledStatusException()
        {
            var statusCode = DeadlineReached ? StatusCode.DeadlineExceeded : StatusCode.Cancelled;
            return new RpcException(new Status(statusCode, string.Empty));
        }

        internal void FinishResponse(HttpResponseMessage httpResponseMessage)
        {
            if (ResponseFinished)
            {
                return;
            }

            ResponseFinished = true;

            _httpResponse = httpResponseMessage;

            var status = GetStatusCore(_httpResponse);
            if (status.StatusCode != StatusCode.OK)
            {
                throw new RpcException(status);
            }
        }

        public async Task<Metadata> GetResponseHeadersAsync()
        {
            _httpResponse = await SendTask.ConfigureAwait(false);

            // The task of this method is cached so there is no need to cache the headers here
            return GrpcProtocolHelpers.BuildMetadata(_httpResponse.Headers);
        }

        public Status GetStatus()
        {
            ValidateTrailersAvailable();

            return GetStatusCore(_httpResponse);
        }

        private static Status GetStatusCore(HttpResponseMessage httpResponseMessage)
        {
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

            string grpcMessage = null;
            if (httpResponseMessage.TrailingHeaders.TryGetValues(GrpcProtocolConstants.MessageTrailer, out var grpcMessageValues))
            {
                grpcMessage = grpcMessageValues.FirstOrDefault();
            }

            return new Status((StatusCode)statusValue, grpcMessage);
        }

        public Metadata GetTrailers()
        {
            if (_trailers == null)
            {
                ValidateTrailersAvailable();

                _trailers = GrpcProtocolHelpers.BuildMetadata(SendTask.Result.TrailingHeaders);
            }

            return _trailers;
        }

        private void ValidateTrailersAvailable()
        {
            // Async call could have been disposed
            EnsureNotDisposed();

            // HttpClient.SendAsync could have failed
            if (SendTask.IsFaulted)
            {
                throw new InvalidOperationException("Can't get the call trailers because an error occured when making the request.", SendTask.Exception);
            }

            // Response could still be in progress
            if (!ResponseFinished || !SendTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Can't get the call trailers because the call is not complete.");
            }
        }
    }
}
