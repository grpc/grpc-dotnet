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
using System.Diagnostics.CodeAnalysis;
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
        private readonly TaskCompletionSource<Status> _callTcs;
        private readonly TimeSpan? _timeout;
        private readonly Uri _uri;
        private readonly GrpcCallScope _logScope;

        private Timer? _deadlineTimer;
        private Metadata? _trailers;
        private CancellationTokenRegistration? _ctsRegistration;
        private TaskCompletionSource<Stream>? _writeStreamTcs;
        private TaskCompletionSource<bool>? _writeCompleteTcs;

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
            _callTcs = new TaskCompletionSource<Status>(TaskContinuationOptions.RunContinuationsAsynchronously);
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

        public Task<Status> CallTask => _callTcs.Task;

        public CancellationToken CancellationToken
        {
            get { return _callCts.Token; }
        }

        public void StartUnary(TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            _ = StartAsync(message);
        }

        public void StartClientStreaming()
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            _ = StartAsync(message);
        }

        public void StartServerStreaming(TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            _ = StartAsync(message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void StartDuplexStreaming()
        {
            var message = CreateHttpRequestMessage();
            ClientStreamWriter = CreateWriter(message);
            _ = StartAsync(message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
        }

        public void Dispose()
        {
            using (StartScope())
            {
                Disposed = true;

                Cleanup(new Status(StatusCode.Cancelled, "gRPC call disposed."));
            }
        }

        /// <summary>
        /// Clean up can be called by:
        /// 1. The user. AsyncUnaryCall.Dispose et al will call this on Dispose
        /// 2. <see cref="ValidateHeaders"/> will call dispose if errors fail validation
        /// 3. <see cref="FinishResponse"/> will call dispose
        /// </summary>
        private void Cleanup(Status status)
        {
            if (!ResponseFinished)
            {
                // If the response is not finished then cancel any pending actions:
                // 1. Call HttpClient.SendAsync
                // 2. Response Stream.ReadAsync
                // 3. Client stream
                //    - Getting the Stream from the Request.HttpContent
                //    - Holding the Request.HttpContent.SerializeToStream open
                //    - Writing to the client stream
                CancelCall(status);
            }
            else
            {
                _writeStreamTcs?.TrySetCanceled();
                _writeCompleteTcs?.TrySetCanceled();

                // If response has successfully finished then the status will come from the trailers
                // If it didn't finish then complete with a status
                _callTcs.TrySetResult(status);
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

        public void EnsureNotDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcCall<TRequest, TResponse>));
            }
        }

        public Exception CreateCanceledStatusException()
        {
            var status = (CallTask.IsCompletedSuccessfully) ? CallTask.Result : new Status(StatusCode.Cancelled, string.Empty);
            return new RpcException(status);
        }

        /// <summary>
        /// Marks the response as finished, i.e. all response content has been read and trailers are available.
        /// Can be called by <see cref="GetResponseAsync"/> for unary and client streaming calls, or
        /// <see cref="HttpContentClientStreamReader{TRequest,TResponse}.MoveNextCore(CancellationToken)"/>
        /// for server streaming and duplex streaming calls.
        /// </summary>
        public void FinishResponse(bool throwOnFail, Status? status = null)
        {
            ResponseFinished = true;
            Debug.Assert(HttpResponse != null);

            if (status == null)
            {
                try
                {
                    if (!TryGetStatusCore(HttpResponse, out status))
                    {
                        status = new Status(StatusCode.Cancelled, "No grpc-status found on response.");
                    }
                }
                catch (Exception ex)
                {
                    // Handle error from parsing badly formed status
                    status = new Status(StatusCode.Cancelled, ex.Message);
                }
            }

            // Clean up call resources once this call is finished
            // Call may not be explicitly disposed when used with unary methods
            // e.g. var reply = await client.SayHelloAsync(new HelloRequest());
            Cleanup(status.Value);

            // Get status from response before dispose
            // This may throw an error if the grpc-status is missing or malformed
            if (status.Value.StatusCode != StatusCode.OK)
            {
                if (throwOnFail)
                {
                    throw new RpcException(status.Value);
                }
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
            catch (OperationCanceledException) when (!Channel.ThrowOperationCanceledExceptionOnCancellation)
            {
                throw CreateCanceledStatusException();
            }
        }

        public Status GetStatus()
        {
            using (StartScope())
            {
                if (CallTask.IsCompletedSuccessfully)
                {
                    return CallTask.Result;
                }

                throw new InvalidOperationException("Unable to get the status because the call is not complete.");
            }
        }

        public async Task<TResponse> GetResponseAsync()
        {
            Debug.Assert(SendTask != null);

            try
            {
                using (StartScope())
                {
                    // Wait for send to finish so the HttpResponse is available
                    await SendTask.ConfigureAwait(false);

                    // Verify the call is not complete. The call should be complete once the grpc-status
                    // has been read from trailers, which happens AFTER the message has been read.
                    if (CallTask.IsCompletedSuccessfully)
                    {
                        var status = CallTask.Result;
                        if (status.StatusCode != StatusCode.OK)
                        {
                            throw new RpcException(status);
                        }
                        else
                        {
                            // The server should never return StatusCode.OK in the header for a unary call.
                            // If it does then throw an error that no message was returned from the server.
                            Log.MessageNotReturned(Logger);
                            throw new InvalidOperationException("Call did not return a response message");
                        }
                    }

                    Debug.Assert(HttpResponse != null);

                    // Trailers are only available once the response body had been read
                    var responseStream = await HttpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var message = await responseStream.ReadSingleMessageAsync(
                        Logger,
                        Method.ResponseMarshaller.ContextualDeserializer,
                        GrpcProtocolHelpers.GetGrpcEncoding(HttpResponse),
                        Channel.ReceiveMaxMessageSize,
                        Channel.CompressionProviders,
                        _callCts.Token).ConfigureAwait(false);
                    FinishResponse(throwOnFail: true);

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
            catch (OperationCanceledException) when (!Channel.ThrowOperationCanceledExceptionOnCancellation)
            {
                throw CreateCanceledStatusException();
            }
        }

        private void ValidateHeaders()
        {
            // We don't want to throw in this method, even for non-success situations.
            // Response is still needed to return headers in GetResponseHeadersAsync.

            Log.ResponseHeadersReceived(Logger);

            Debug.Assert(HttpResponse != null);
            if (HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                FinishResponse(throwOnFail: false, new Status(StatusCode.Cancelled, "Bad gRPC response. Expected HTTP status code 200. Got status code: " + (int)HttpResponse.StatusCode));
                return;
            }
            
            if (HttpResponse.Content?.Headers.ContentType == null)
            {
                FinishResponse(throwOnFail: false, new Status(StatusCode.Cancelled, "Bad gRPC response. Response did not have a content-type header."));
                return;
            }

            var grpcEncoding = HttpResponse.Content.Headers.ContentType.ToString();
            if (!GrpcProtocolHelpers.IsGrpcContentType(grpcEncoding))
            {
                FinishResponse(throwOnFail: false, new Status(StatusCode.Cancelled, "Bad gRPC response. Invalid content-type value: " + grpcEncoding));
                return;
            }
            else
            {
                if (TryGetStatusCore(HttpResponse.Headers, out var status))
                {
                    // grpc-status is returned in the header when there is no message body
                    // For example, unimplemented method/service status
                    FinishResponse(throwOnFail: false, status);
                    return;
                }
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
                        Channel.CompressionProviders,
                        Options);
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

        private void CancelCall(Status status)
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

            // If response has successfully finished then the status will come from the trailers
            // If it didn't finish then complete with a status
            _callTcs.TrySetResult(status);
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

        private async Task StartAsync(HttpRequestMessage request)
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

                SendTask = SendAsync(request);

                // Wait until the call is complete
                // TCS will be set in Dispose
                var status = await CallTask.ConfigureAwait(false);

                if (status.StatusCode != StatusCode.OK)
                {
                    Log.GrpcStatusError(Logger, status.StatusCode, status.Detail);
                    GrpcEventSource.Log.CallFailed(status.StatusCode);
                }
                Log.FinishedCall(Logger);
                GrpcEventSource.Log.CallStop();

                // Activity needs to be stopped in the same execution context it was started
                if (activity != null)
                {
                    var statusText = status.StatusCode.ToString("D");
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

        private async Task SendAsync(HttpRequestMessage request)
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
                        CancelCall(new Status(StatusCode.Cancelled, "Call canceled by the client."));
                    }
                });
            }

            if (Options.Credentials != null || Channel.CallCredentials?.Count > 0)
            {
                // In C-Core the call credential auth metadata is only applied if the channel is secure
                // The equivalent in grpc-dotnet is only applying metadata if HttpClient is using TLS
                // HttpClient scheme will be HTTP if it is using H2C (HTTP2 without TLS)
                if (Channel.Address.Scheme == Uri.UriSchemeHttps)
                {
                    var configurator = new DefaultCallCredentialsConfigurator();

                    if (Options.Credentials != null)
                    {
                        await ReadCredentialMetadata(configurator, Channel, request, Options.Credentials).ConfigureAwait(false);
                    }
                    if (Channel.CallCredentials?.Count > 0)
                    {
                        foreach (var credentials in Channel.CallCredentials)
                        {
                            await ReadCredentialMetadata(configurator, Channel, request, credentials).ConfigureAwait(false);
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
                HttpResponse = await Channel.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _callCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.ErrorStartingCall(Logger, ex);
                CancelCall(new Status(StatusCode.Cancelled, "Error starting gRPC call."));
                throw;
            }

            ValidateHeaders();
        }

        private async Task ReadCredentialMetadata(
            DefaultCallCredentialsConfigurator configurator,
            GrpcChannel channel,
            HttpRequestMessage message,
            CallCredentials credentials)
        {
            credentials.InternalPopulateConfiguration(configurator, null);

            if (configurator.Interceptor != null)
            {
                var authInterceptorContext = CreateAuthInterceptorContext(channel.Address, Method);
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
                    await ReadCredentialMetadata(configurator, channel, message, c).ConfigureAwait(false);
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
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Channel.Address, _uri));
            message.Version = new Version(2, 0);
            // User agent is optional but recommended
            message.Headers.UserAgent.Add(GrpcProtocolConstants.UserAgentHeader);
            // TE is required by some servers, e.g. C Core
            // A missing TE header results in servers aborting the gRPC call
            message.Headers.TE.Add(GrpcProtocolConstants.TEHeader);
            message.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, Channel.MessageAcceptEncoding);

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
                        // grpc-internal-encoding-request is used in the client to set message compression.
                        // 'grpc-encoding' is sent even if WriteOptions.Flags = NoCompress. In that situation
                        // individual messages will not be written with compression.
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

                CancelCall(new Status(StatusCode.DeadlineExceeded, string.Empty));
            }
        }

        private static bool TryGetStatusCore(HttpResponseMessage httpResponseMessage, [NotNullWhen(true)]out Status? status)
        {
            if (TryGetStatusCore(httpResponseMessage.TrailingHeaders, out status))
            {
                return true;
            }

            // A gRPC server may return gRPC status in the headers when the response stream is empty
            // For example, C Core server returns them together in the empty_stream interop test
            if (TryGetStatusCore(httpResponseMessage.Headers, out status))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetStatusCore(HttpResponseHeaders headers, [NotNullWhen(true)]out Status? status)
        {
            var grpcStatus = GetHeaderValue(headers, GrpcProtocolConstants.StatusTrailer);

            // grpc-status is a required trailer
            if (grpcStatus == null)
            {
                status = null;
                return false;
            }

            int statusValue;
            if (!int.TryParse(grpcStatus, out statusValue))
            {
                throw new InvalidOperationException("Unexpected grpc-status value: " + grpcStatus);
            }

            // grpc-message is optional
            // Always read the gRPC message from the same headers collection as the status
            var grpcMessage = GetHeaderValue(headers, GrpcProtocolConstants.MessageTrailer);

            if (!string.IsNullOrEmpty(grpcMessage))
            {
                // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
                // The value portion of Status-Message is conceptually a Unicode string description of the error,
                // physically encoded as UTF-8 followed by percent-encoding.
                grpcMessage = Uri.UnescapeDataString(grpcMessage);
            }

            status = new Status((StatusCode)statusValue, grpcMessage);
            return true;
        }

        private static string? GetHeaderValue(HttpHeaders? headers, string name)
        {
            if (headers == null)
            {
                return null;
            }

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
            // Response is finished
            if (ResponseFinished)
            {
                return;
            }

            // HttpClient.SendAsync could have failed
            Debug.Assert(SendTask != null);
            if (SendTask.IsFaulted)
            {
                throw new InvalidOperationException("Can't get the call trailers because an error occured when making the request.", SendTask.Exception);
            }

            // Call could have been canceled or deadline exceeded
            if (_callCts.IsCancellationRequested)
            {
                // Throw InvalidOperationException here because documentation on GetTrailers says that
                // InvalidOperationException is thrown if the call is not complete.
                throw new InvalidOperationException("Can't get the call trailers because the call was canceled.");
            }

            throw new InvalidOperationException("Can't get the call trailers because the call is not complete.");
        }
    }
}
