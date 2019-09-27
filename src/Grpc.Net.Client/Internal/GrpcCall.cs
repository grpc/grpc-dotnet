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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
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
        // Getting logger name from generic type is slow
        private const string LoggerName = "Grpc.Net.Client.Internal.GrpcCall";

        private readonly CancellationTokenSource _callCts;
        private readonly TaskCompletionSource<Status> _callTcs;
        private readonly TaskCompletionSource<Metadata> _metadataTcs;
        private readonly TimeSpan? _timeout;
        private readonly GrpcMethodInfo _grpcMethodInfo;

        private Timer? _deadlineTimer;
        private Metadata? _trailers;
        private CancellationTokenRegistration? _ctsRegistration;

        public bool Disposed { get; private set; }
        public bool ResponseFinished { get; private set; }
        public HttpResponseMessage? HttpResponse { get; private set; }
        public CallOptions Options { get; }
        public Method<TRequest, TResponse> Method { get; }
        public GrpcChannel Channel { get; }
        public ILogger Logger { get; }

        // These are set depending on the type of gRPC call
        private TaskCompletionSource<TResponse>? _responseTcs;
        public HttpContentClientStreamWriter<TRequest, TResponse>? ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse>? ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, GrpcMethodInfo grpcMethodInfo, CallOptions options, GrpcChannel channel)
        {
            // Validate deadline before creating any objects that require cleanup
            ValidateDeadline(options.Deadline);

            _callCts = new CancellationTokenSource();
            // Run the callTcs continuation immediately to keep the same context. Required for Activity.
            _callTcs = new TaskCompletionSource<Status>();
            _metadataTcs = new TaskCompletionSource<Metadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            Method = method;
            _grpcMethodInfo = grpcMethodInfo;
            Options = options;
            Channel = channel;
            Logger = channel.LoggerFactory.CreateLogger(LoggerName);

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
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            _ = RunCall(message);
        }

        public void StartClientStreaming()
        {
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var message = CreateHttpRequestMessage();
            CreateWriter(message);
            _ = RunCall(message);
        }

        public void StartServerStreaming(TRequest request)
        {
            var message = CreateHttpRequestMessage();
            SetMessageContent(request, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message);
        }

        public void StartDuplexStreaming()
        {
            var message = CreateHttpRequestMessage();
            CreateWriter(message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message);
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
                _callTcs.TrySetResult(status);

                ClientStreamWriter?.WriteStreamTcs.TrySetCanceled();
                ClientStreamWriter?.CompleteTcs.TrySetCanceled();
                ClientStreamReader?.HttpResponseTcs.TrySetCanceled();
            }

            _ctsRegistration?.Dispose();
            _deadlineTimer?.Dispose();
            HttpResponse?.Dispose();
            ClientStreamReader?.Dispose();
            ClientStreamWriter?.Dispose();

            // To avoid racing with Dispose, skip disposing the call CTS.
            // This avoid Dispose potentially calling cancel on a disposed CTS.
            // The call CTS is not exposed externally and all dependent registrations
            // are cleaned up.
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
        public void FinishResponse(Status status)
        {
            ResponseFinished = true;

            // Clean up call resources once this call is finished
            // Call may not be explicitly disposed when used with unary methods
            // e.g. var reply = await client.SayHelloAsync(new HelloRequest());
            Cleanup(status);
        }

        public Task<Metadata> GetResponseHeadersAsync()
        {
            return _metadataTcs.Task;
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

        public Task<TResponse> GetResponseAsync()
        {
            Debug.Assert(_responseTcs != null);
            return _responseTcs.Task;
        }

        private Status? ValidateHeaders(HttpResponseMessage httpResponse)
        {
            Log.ResponseHeadersReceived(Logger);

            if (httpResponse.StatusCode != HttpStatusCode.OK)
            {
                return new Status(StatusCode.Cancelled, "Bad gRPC response. Expected HTTP status code 200. Got status code: " + (int)httpResponse.StatusCode);
            }
            
            if (httpResponse.Content?.Headers.ContentType == null)
            {
                return new Status(StatusCode.Cancelled, "Bad gRPC response. Response did not have a content-type header.");
            }

            var grpcEncoding = httpResponse.Content.Headers.ContentType;
            if (!GrpcProtocolHelpers.IsGrpcContentType(grpcEncoding))
            {
                return new Status(StatusCode.Cancelled, "Bad gRPC response. Invalid content-type value: " + grpcEncoding);
            }
            else
            {
                // gRPC status can be returned in the header when there is no message (e.g. unimplemented status)
                if (GrpcProtocolHelpers.TryGetStatusCore(httpResponse.Headers, out var status))
                {
                    return status;
                }
            }

            // Call is still in progress
            return null;
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
            message.Content = new PushUnaryContent<TRequest, TResponse>(
                request,
                this,
                GrpcProtocolHelpers.GetRequestEncoding(message.Headers),
                GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        private void CancelCall(Status status)
        {
            // Set overall call status first. Status can be used in throw RpcException from cancellation.
            // If response has successfully finished then the status will come from the trailers.
            // If it didn't finish then complete with a status.
            _callTcs.TrySetResult(status);

            // Checking if cancellation has already happened isn't threadsafe
            // but there is no adverse effect other than an extra log message
            if (!_callCts.IsCancellationRequested)
            {
                Log.CanceledCall(Logger);

                // Cancel in-progress HttpClient.SendAsync and Stream.ReadAsync tasks.
                // Cancel will send RST_STREAM if HttpClient.SendAsync isn't complete.
                // Cancellation will also cause reader/writer to throw if used afterwards.
                _callCts.Cancel();

                // Cancellation token won't send RST_STREAM if HttpClient.SendAsync is complete.
                // Dispose HttpResponseMessage to send RST_STREAM to server for in-progress calls.
                HttpResponse?.Dispose();

                // Canceling call will cancel pending writes to the stream
                ClientStreamWriter?.WriteStreamTcs.TrySetCanceled();
                ClientStreamWriter?.CompleteTcs.TrySetCanceled();
                ClientStreamReader?.HttpResponseTcs.TrySetCanceled();

                if (!Channel.ThrowOperationCanceledOnCancellation)
                {
                    _metadataTcs.TrySetException(new RpcException(status));
                }
                else
                {
                    _metadataTcs.TrySetCanceled(_callCts.Token);
                }
            }
        }

        internal IDisposable? StartScope()
        {
            // Only return a scope if the logger is enabled to log 
            // in at least Critical level for performance
            if (Logger.IsEnabled(LogLevel.Critical))
            {
                return Logger.BeginScope(_grpcMethodInfo.LogScope);
            }

            return null;
        }

        private async ValueTask RunCall(HttpRequestMessage request)
        {
            using (StartScope())
            {
                var (diagnosticSourceEnabled, activity) = InitializeCall(request);

                if (Options.Credentials != null || Channel.CallCredentials?.Count > 0)
                {
                    await ReadCredentials(request).ConfigureAwait(false);
                }

                Status? status = null;

                try
                {
                    try
                    {
                        HttpResponse = await Channel.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _callCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorStartingCall(Logger, ex);
                        throw;
                    }

                    BuildMetadata(HttpResponse);

                    status = ValidateHeaders(HttpResponse);

                    // A status means either the call has failed or grpc-status was returned in the response header
                    if (status != null)
                    {
                        if (_responseTcs != null)
                        {
                            // gRPC status in the header
                            if (status.Value.StatusCode != StatusCode.OK)
                            {
                                SetFailedResult(status.Value);
                            }
                            else
                            {
                                // The server should never return StatusCode.OK in the header for a unary call.
                                // If it does then throw an error that no message was returned from the server.
                                Log.MessageNotReturned(Logger);
                                _responseTcs.TrySetException(new InvalidOperationException("Call did not return a response message."));
                            }
                        }

                        FinishResponse(status.Value);
                    }
                    else
                    {
                        if (_responseTcs != null)
                        {
                            // Read entire response body immediately and read status from trailers
                            // Trailers are only available once the response body had been read
                            var responseStream = await HttpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            var message = await responseStream.ReadSingleMessageAsync(
                                Logger,
                                Method.ResponseMarshaller.ContextualDeserializer,
                                GrpcProtocolHelpers.GetGrpcEncoding(HttpResponse),
                                Channel.ReceiveMaxMessageSize,
                                Channel.CompressionProviders,
                                _callCts.Token).ConfigureAwait(false);
                            status = GrpcProtocolHelpers.GetResponseStatus(HttpResponse);
                            FinishResponse(status.Value);

                            if (message == null)
                            {
                                Log.MessageNotReturned(Logger);
                                SetFailedResult(status.Value);
                            }
                            else
                            {
                                GrpcEventSource.Log.MessageReceived();

                                if (status.Value.StatusCode == StatusCode.OK)
                                {
                                    _responseTcs.TrySetResult(message);
                                }
                                else
                                {
                                    SetFailedResult(status.Value);
                                }
                            }
                        }
                        else
                        {
                            // Duplex or server streaming call
                            Debug.Assert(ClientStreamReader != null);
                            ClientStreamReader.HttpResponseTcs.TrySetResult((HttpResponse, status));

                            // Wait until the response has been read and status read from trailers.
                            // TCS will also be set in Dispose.
                            status = await CallTask.ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Exception resolvedException;
                    if (ex is OperationCanceledException)
                    {
                        status = (CallTask.IsCompletedSuccessfully) ? CallTask.Result : new Status(StatusCode.Cancelled, string.Empty);
                        resolvedException = Channel.ThrowOperationCanceledOnCancellation ? ex : new RpcException(status.Value);
                    }
                    else if (ex is RpcException rpcException)
                    {
                        status = rpcException.Status;
                        resolvedException = new RpcException(status.Value);
                    }
                    else
                    {
                        status = new Status(StatusCode.Internal, "Error starting gRPC call: " + ex.Message);
                        resolvedException = new RpcException(status.Value);
                    }

                    _metadataTcs.TrySetException(resolvedException);
                    _responseTcs?.TrySetException(resolvedException);

                    Cleanup(status!.Value);
                }

                FinishCall(request, diagnosticSourceEnabled, activity, status);
            }
        }

        private void SetFailedResult(Status status)
        {
            Debug.Assert(_responseTcs != null);

            if (Channel.ThrowOperationCanceledOnCancellation && status.StatusCode == StatusCode.DeadlineExceeded)
            {
                // Convert status response of DeadlineExceeded to OperationCanceledException when
                // ThrowOperationCanceledOnCancellation is true.
                // This avoids a race between the client-side timer and the server status throwing different
                // errors on deadline exceeded.
                _responseTcs.TrySetCanceled();
            }
            else
            {
                _responseTcs.TrySetException(new RpcException(status));
            }
        }

        public Exception CreateFailureStatusException(Status status)
        {
            if (Channel.ThrowOperationCanceledOnCancellation && status.StatusCode == StatusCode.DeadlineExceeded)
            {
                // Convert status response of DeadlineExceeded to OperationCanceledException when
                // ThrowOperationCanceledOnCancellation is true.
                // This avoids a race between the client-side timer and the server status throwing different
                // errors on deadline exceeded.
                return new OperationCanceledException();
            }
            else
            {
                return new RpcException(status);
            }
        }

        private void BuildMetadata(HttpResponseMessage httpResponse)
        {
            try
            {
                _metadataTcs.TrySetResult(GrpcProtocolHelpers.BuildMetadata(httpResponse.Headers));
            }
            catch (Exception ex)
            {
                _metadataTcs.TrySetException(ex);
            }
        }

        private (bool diagnosticSourceEnabled, Activity? activity) InitializeCall(HttpRequestMessage request)
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

            var diagnosticSourceEnabled = GrpcDiagnostics.DiagnosticListener.IsEnabled() &&
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

            return (diagnosticSourceEnabled, activity);
        }

        private void FinishCall(HttpRequestMessage request, bool diagnosticSourceEnabled, Activity? activity, Status? status)
        {
            if (status!.Value.StatusCode != StatusCode.OK)
            {
                Log.GrpcStatusError(Logger, status.Value.StatusCode, status.Value.Detail);
                GrpcEventSource.Log.CallFailed(status.Value.StatusCode);
            }
            Log.FinishedCall(Logger);
            GrpcEventSource.Log.CallStop();

            // Activity needs to be stopped in the same execution context it was started
            if (activity != null)
            {
                var statusText = status.Value.StatusCode.ToString("D");
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

        private async Task ReadCredentials(HttpRequestMessage request)
        {
            // In C-Core the call credential auth metadata is only applied if the channel is secure
            // The equivalent in grpc-dotnet is only applying metadata if HttpClient is using TLS
            // HttpClient scheme will be HTTP if it is using H2C (HTTP2 without TLS)
            if (Channel.Address.Scheme == Uri.UriSchemeHttps)
            {
                var configurator = new DefaultCallCredentialsConfigurator();

                if (Options.Credentials != null)
                {
                    await GrpcProtocolHelpers.ReadCredentialMetadata(configurator, Channel, request, Method, Options.Credentials).ConfigureAwait(false);
                }
                if (Channel.CallCredentials?.Count > 0)
                {
                    foreach (var credentials in Channel.CallCredentials)
                    {
                        await GrpcProtocolHelpers.ReadCredentialMetadata(configurator, Channel, request, Method, credentials).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                Log.CallCredentialsNotUsed(Logger);
            }
        }

        private void CreateWriter(HttpRequestMessage message)
        {
            ClientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(this, message);

            message.Content = new PushStreamContent<TRequest, TResponse>(ClientStreamWriter, GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        private HttpRequestMessage CreateHttpRequestMessage()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _grpcMethodInfo.CallUri);
            message.Version = GrpcProtocolConstants.ProtocolVersion;

            // Set raw headers on request using name/values. Typed headers allocate additional objects.
            var headers = message.Headers;

            // User agent is optional but recommended.
            headers.Add(GrpcProtocolConstants.UserAgentHeader, GrpcProtocolConstants.UserAgentHeaderValue);
            // TE is required by some servers, e.g. C Core.
            // A missing TE header results in servers aborting the gRPC call.
            headers.Add(GrpcProtocolConstants.TEHeader, GrpcProtocolConstants.TEHeaderValue);
            headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, Channel.MessageAcceptEncoding);

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
                        headers.Add(GrpcProtocolConstants.MessageEncodingHeader, entry.Value);
                    }
                    else
                    {
                        GrpcProtocolHelpers.AddHeader(headers, entry);
                    }
                }
            }

            if (_timeout != null)
            {
                headers.Add(GrpcProtocolConstants.TimeoutHeader, GrpcProtocolHelpers.EncodeTimeout(Convert.ToInt64(_timeout.Value.TotalMilliseconds)));
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
                GrpcEventSource.Log.CallDeadlineExceeded();

                CancelCall(new Status(StatusCode.DeadlineExceeded, string.Empty));
            }
        }

        private void ValidateTrailersAvailable()
        {
            // Response is finished
            if (ResponseFinished)
            {
                return;
            }

            // Throw InvalidOperationException here because documentation on GetTrailers says that
            // InvalidOperationException is thrown if the call is not complete.
            throw new InvalidOperationException("Can't get the call trailers because the call has not completed successfully.");
        }
    }
}
