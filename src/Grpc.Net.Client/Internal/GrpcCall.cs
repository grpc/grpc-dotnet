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
using System.Collections.Generic;
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
        private readonly TaskCompletionSource<StatusCode> _callTcs;
        private readonly TimeSpan? _timeout;
        private readonly Uri _uri;
        private readonly GrpcCallScope _logScope;

        private Timer? _deadlineTimer;
        private Metadata? _trailers;
        private string? _headerValidationError;
        private CancellationTokenRegistration? _ctsRegistration;
        private TaskCompletionSource<Stream>? _writeStreamTcs;
        private TaskCompletionSource<bool>? _writeCompleteTcs;

        public bool DeadlineReached { get; private set; }
        public bool Disposed { get; private set; }
        public bool ResponseFinished { get; private set; }
        public HttpResponseMessage? HttpResponse { get; private set; }
        public CallOptions Options { get; }
        public Method<TRequest, TResponse> Method { get; }
        public GrpcChannel Channel { get; }

        public ILogger Logger { get; }
        public Task? SendTask { get; private set; }
        public HttpContentClientStreamWriter<TRequest, TResponse>? ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse>? ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, CallOptions options, GrpcChannel channel)
        {
            // Validate deadline before creating any objects that require cleanup
            ValidateDeadline(options.Deadline);

            _callCts = new CancellationTokenSource();
            _callTcs = new TaskCompletionSource<StatusCode>(TaskContinuationOptions.RunContinuationsAsynchronously);
            Method = method;
            _uri = new Uri(method.FullName, UriKind.Relative);
            _logScope = new GrpcCallScope(method.Type, _uri);
            Options = options;
            Channel = channel;
            Logger = channel.LoggerFactory.CreateLogger<GrpcCall<TRequest, TResponse>>();

            if (options.Deadline != null && options.Deadline != DateTime.MaxValue)
            {
                var timeout = options.Deadline.GetValueOrDefault() - Channel.Clock.UtcNow;
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
            _ = StartAsync(client, message);
        }

        public void StartClientStreaming(HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            _ = StartAsync(client, message);
        }

        public void StartServerStreaming(HttpClient client, TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            _ = StartAsync(client, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void StartDuplexStreaming(HttpClient client)
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            _ = StartAsync(client, message);
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
                // Locking on the call because:
                // 1. Its not exposed publically
                // 2. Nothing else locks on call
                // 3. We want to avoid allocating a private lock object
                lock (this)
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
                            _writeCompleteTcs?.TrySetCanceled();
                        }

                        // If response has successfully finished then the status will come from the trailers
                        // If it didn't finish then complete with a Cancelled status
                        _callTcs.TrySetResult(StatusCode.Cancelled);

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
                    var message = await responseStream.ReadSingleMessageAsync(
                        Logger,
                        Method.ResponseMarshaller.ContextualDeserializer,
                        GrpcProtocolHelpers.GetGrpcEncoding(HttpResponse),
                        Channel.ReceiveMaxMessageSize,
                        _callCts.Token).ConfigureAwait(false);
                    FinishResponse();

                    if (message == null)
                    {
                        Log.MessageNotReturned(Logger);
                        throw new InvalidOperationException("Call did not return a response message");
                    }

                    GrpcEventSource.Log.MessageReceived();

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
                    var grpcEncoding = GrpcProtocolHelpers.GetRequestEncoding(message.Headers);

                    var writeMessageTask = stream.WriteMessageAsync<TRequest>(
                        Logger,
                        request,
                        Method.RequestMarshaller.ContextualSerializer,
                        grpcEncoding,
                        Channel.SendMaxMessageSize,
                        Options.CancellationToken);
                    if (writeMessageTask.IsCompletedSuccessfully)
                    {
                        GrpcEventSource.Log.MessageSent();
                        return Task.CompletedTask;
                    }

                    return WriteMessageCore(writeMessageTask);
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);

            static async Task WriteMessageCore(Task writeMessageTask)
            {
                await writeMessageTask.ConfigureAwait(false);
                GrpcEventSource.Log.MessageSent();
            }
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
                _writeCompleteTcs?.TrySetCanceled();
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

        private async Task StartAsync(HttpClient client, HttpRequestMessage request)
        {
            using (StartScope())
            {
                if (_timeout != null && !Channel.DisableClientDeadlineTimer)
                {
                    Log.StartingDeadlineTimeout(Logger, _timeout.Value);

                    // Deadline timer will cancel the call CTS
                    // Start timer after reader/writer have been created, otherwise a zero length deadline could cancel
                    // the call CTS before they are created and leave them in a non-canceled state
                    _deadlineTimer = new Timer(DeadlineExceeded, null, _timeout.Value, Timeout.InfiniteTimeSpan);
                }

                Log.StartingCall(Logger, Method.Type, request.RequestUri);
                GrpcEventSource.Log.CallStart(Method.FullName);

                var diagnosticSourceEnabled =
                    GrpcDiagnostics.DiagnosticListener.IsEnabled() &&
                    GrpcDiagnostics.DiagnosticListener.IsEnabled(GrpcDiagnostics.ActivityName, request);

                Activity? activity = null;

                // Set activity if:
                // 1. Diagnostic source is enabled
                // 2. Logging is enabled
                // 3. There is an existing activity (to enable activity propagation)
                if (diagnosticSourceEnabled || Logger.IsEnabled(LogLevel.Critical) || Activity.Current != null)
                {
                    activity = new Activity(GrpcDiagnostics.ActivityName);
                    activity.AddTag(GrpcDiagnostics.GrpcMethodTagName, Method.FullName);
                    activity.Start();

                    if (diagnosticSourceEnabled)
                    {
                        GrpcDiagnostics.DiagnosticListener.Write(GrpcDiagnostics.ActivityStartKey, new { Request = request });
                    }
                }

                SendTask = SendAsync(client, request);

                // Wait until the call is complete
                // TCS will be set in Dispose
                var statusCode = await _callTcs.Task.ConfigureAwait(false);

                if (statusCode != StatusCode.OK)
                {
                    GrpcEventSource.Log.CallFailed(statusCode);
                }
                GrpcEventSource.Log.CallStop();

                // Activity needs to be stopped in the same execution context it was started
                if (activity != null)
                {
                    var statusText = statusCode.ToString("D");
                    if (statusText != null)
                    {
                        activity.AddTag(GrpcDiagnostics.GrpcStatusCodeTagName, statusText);
                    }

                    if (diagnosticSourceEnabled)
                    {
                        // Stop sets the end time if it was unset, but we want it set before we issue the write
                        // so we do it now.   
                        if (activity.Duration == TimeSpan.Zero)
                        {
                            activity.SetEndTime(DateTime.UtcNow);
                        }

                        GrpcDiagnostics.DiagnosticListener.Write(GrpcDiagnostics.ActivityStopKey, new { Request = request, Response = HttpResponse });
                    }

                    activity.Stop();
                }
            }
        }

        private async Task SendAsync(HttpClient client, HttpRequestMessage request)
        {
            if (Options.CancellationToken.CanBeCanceled)
            {
                // The cancellation token will cancel the call CTS.
                // This must be registered after the client writer has been created
                // so that cancellation will always complete the writer.
                _ctsRegistration = Options.CancellationToken.Register(() =>
                {
                    using (StartScope())
                    {
                        CancelCall();
                    }
                });
            }

            if (Options.Credentials != null || Channel.CallCredentials?.Count > 0)
            {
                // In C-Core the call credential auth metadata is only applied if the channel is secure
                // The equivalent in grpc-dotnet is only applying metadata if HttpClient is using TLS
                // HttpClient scheme will be HTTP if it is using H2C (HTTP2 without TLS)
                if (client.BaseAddress.Scheme == Uri.UriSchemeHttps)
                {
                    var configurator = new DefaultCallCredentialsConfigurator();

                    if (Options.Credentials != null)
                    {
                        await ReadCredentialMetadata(configurator, client, request, Options.Credentials).ConfigureAwait(false);
                    }
                    if (Channel.CallCredentials?.Count > 0)
                    {
                        foreach (var credentials in Channel.CallCredentials)
                        {
                            await ReadCredentialMetadata(configurator, client, request, credentials).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    Log.CallCredentialsNotUsed(Logger);
                }
            }

            try
            {
                HttpResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _callCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.ErrorStartingCall(Logger, ex);
                throw;
            }

            ValidateHeaders();
        }

        private async Task ReadCredentialMetadata(
            DefaultCallCredentialsConfigurator configurator,
            HttpClient client,
            HttpRequestMessage message,
            CallCredentials credentials)
        {
            credentials.InternalPopulateConfiguration(configurator, null);

            if (configurator.Interceptor != null)
            {
                var authInterceptorContext = CreateAuthInterceptorContext(client.BaseAddress, Method);
                var metadata = new Metadata();
                await configurator.Interceptor(authInterceptorContext, metadata).ConfigureAwait(false);

                foreach (var entry in metadata)
                {
                    AddHeader(message.Headers, entry);
                }
            }

            if (configurator.Credentials != null)
            {
                // Copy credentials locally. ReadCredentialMetadata will update it.
                var callCredentials = configurator.Credentials;
                foreach (var c in callCredentials)
                {
                    configurator.Reset();
                    await ReadCredentialMetadata(configurator, client, message, c).ConfigureAwait(false);
                }
            }
        }

        private static AuthInterceptorContext CreateAuthInterceptorContext(Uri baseAddress, IMethod method)
        {
            var authority = baseAddress.Authority;
            if (baseAddress.Scheme == Uri.UriSchemeHttps && authority.EndsWith(":443", StringComparison.Ordinal))
            {
                // The service URL can be used by auth libraries to construct the "aud" fields of the JWT token,
                // so not producing serviceUrl compatible with other gRPC implementations can lead to auth failures.
                // For https and the default port 443, the port suffix should be stripped.
                // https://github.com/grpc/grpc/blob/39e982a263e5c48a650990743ed398c1c76db1ac/src/core/lib/security/transport/client_auth_filter.cc#L205
                authority = authority.Substring(0, authority.Length - 4);
            }
            var serviceUrl = baseAddress.Scheme + "://" + authority + baseAddress.AbsolutePath;
            if (!serviceUrl.EndsWith("/", StringComparison.Ordinal))
            {
                serviceUrl += "/";
            }
            serviceUrl += method.ServiceName;
            return new AuthInterceptorContext(serviceUrl, method.Name);
        }

        private HttpContentClientStreamWriter<TRequest, TResponse> CreateWriter(HttpRequestMessage message)
        {
            _writeStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _writeCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            message.Content = new PushStreamContent(
                async stream =>
                {
                    // Immediately flush request stream to send headers
                    // https://github.com/dotnet/corefx/issues/39586#issuecomment-516210081
                    await stream.FlushAsync().ConfigureAwait(false);

                    // Pass request stream to writer
                    _writeStreamTcs.TrySetResult(stream);

                    // Wait for the writer to report it is complete
                    await _writeCompleteTcs.Task.ConfigureAwait(false);
                },
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);

            var writer = new HttpContentClientStreamWriter<TRequest, TResponse>(this, message, _writeStreamTcs.Task, _writeCompleteTcs);
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
            message.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, GrpcProtocolConstants.MessageAcceptEncodingValue);

            if (Options.Headers != null && Options.Headers.Count > 0)
            {
                foreach (var entry in Options.Headers)
                {
                    if (entry.Key == GrpcProtocolConstants.TimeoutHeader)
                    {
                        // grpc-timeout is set via CallOptions.Deadline
                        continue;
                    }
                    else if (entry.Key == GrpcProtocolConstants.CompressionRequestAlgorithmHeader)
                    {
                        // grpc-internal-encoding-request is used in the client to set message compression
                        message.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, entry.Value);
                    }
                    else
                    {
                        AddHeader(message.Headers, entry);
                    }
                }
            }

            if (_timeout != null)
            {
                message.Headers.Add(GrpcProtocolConstants.TimeoutHeader, GrpcProtocolHelpers.EncodeTimeout(Convert.ToInt64(_timeout.Value.TotalMilliseconds)));
            }

            return message;
        }

        private static void AddHeader(HttpRequestHeaders headers, Metadata.Entry entry)
        {
            var value = entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value;
            headers.Add(entry.Key, value);
        }

        private class DefaultCallCredentialsConfigurator : CallCredentialsConfiguratorBase
        {
            public AsyncAuthInterceptor? Interceptor { get; private set; }
            public IReadOnlyList<CallCredentials>? Credentials { get; private set; }

            public void Reset()
            {
                Interceptor = null;
                Credentials = null;
            }

            public override void SetAsyncAuthInterceptorCredentials(object state, AsyncAuthInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public override void SetCompositeCredentials(object state, IReadOnlyList<CallCredentials> credentials)
            {
                Credentials = credentials;
            }
        }

        private void DeadlineExceeded(object state)
        {
            // Deadline is only exceeded if the timeout has passed and
            // the response has not been finished or canceled
            if (!_callCts.IsCancellationRequested && !ResponseFinished)
            {
                Log.DeadlineExceeded(Logger);
                GrpcEventSource.Log.CallDeadlineExceeded();

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
