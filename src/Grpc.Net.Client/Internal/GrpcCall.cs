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

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Grpc.Core;
using Grpc.Net.Client.Internal.Http;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
#if SUPPORT_LOAD_BALANCING
using Grpc.Net.Client.Balancer.Internal;
#endif

#if NETSTANDARD2_0
using ValueTask = System.Threading.Tasks.Task;
#endif

namespace Grpc.Net.Client.Internal
{
    internal sealed partial class GrpcCall<TRequest, TResponse> : GrpcCall, IGrpcCall<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        internal const string ErrorStartingCallMessage = "Error starting gRPC call.";

        private readonly CancellationTokenSource _callCts;
        private readonly TaskCompletionSource<HttpResponseMessage> _httpResponseTcs;
        private readonly TaskCompletionSource<Status> _callTcs;
        private readonly GrpcMethodInfo _grpcMethodInfo;
        private readonly int _attemptCount;

        internal Task<HttpResponseMessage> HttpResponseTask => _httpResponseTcs.Task;
        private Task<Metadata>? _responseHeadersTask;
        private Timer? _deadlineTimer;
        private DateTime _deadline;
        private CancellationTokenRegistration? _ctsRegistration;

        public bool Disposed { get; private set; }
        public Method<TRequest, TResponse> Method { get; }

        // These are set depending on the type of gRPC call
        private TaskCompletionSource<TResponse>? _responseTcs;

        public int MessagesWritten { get; private set; }
        public HttpContentClientStreamWriter<TRequest, TResponse>? ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse>? ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, GrpcMethodInfo grpcMethodInfo, CallOptions options, GrpcChannel channel, int attemptCount)
            : base(options, channel)
        {
            // Validate deadline before creating any objects that require cleanup
            ValidateDeadline(options.Deadline);

            _callCts = new CancellationTokenSource();
            _httpResponseTcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Run the callTcs continuation immediately to keep the same context. Required for Activity.
            _callTcs = new TaskCompletionSource<Status>();
            Method = method;
            _grpcMethodInfo = grpcMethodInfo;
            _deadline = options.Deadline ?? DateTime.MaxValue;
            _attemptCount = attemptCount;

            Channel.RegisterActiveCall(this);
        }

        public MethodConfigInfo? MethodConfig => _grpcMethodInfo.MethodConfig;

        private void ValidateDeadline(DateTime? deadline)
        {
            if (deadline != null && deadline != DateTime.MaxValue && deadline != DateTime.MinValue && deadline.Value.Kind != DateTimeKind.Utc)
            {
                throw new InvalidOperationException("Deadline must have a kind DateTimeKind.Utc or be equal to DateTime.MaxValue or DateTime.MinValue.");
            }
        }

        public Task<Status> CallTask => _callTcs.Task;

        public override CancellationToken CancellationToken => _callCts.Token;

        public override Type RequestType => typeof(TRequest);
        public override Type ResponseType => typeof(TResponse);

        IClientStreamWriter<TRequest>? IGrpcCall<TRequest, TResponse>.ClientStreamWriter => ClientStreamWriter;
        IAsyncStreamReader<TResponse>? IGrpcCall<TRequest, TResponse>.ClientStreamReader => ClientStreamReader;

        public void StartUnary(TRequest request) => StartUnaryCore(CreatePushUnaryContent(request));

        public void StartClientStreaming()
        {
            var clientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(this);
            var content = new PushStreamContent<TRequest, TResponse>(clientStreamWriter);

            StartClientStreamingCore(clientStreamWriter, content);
        }

        public void StartServerStreaming(TRequest request) => StartServerStreamingCore(CreatePushUnaryContent(request));

        private HttpContent CreatePushUnaryContent(TRequest request)
        {
            // WinHttp currently doesn't support streaming request data so a length needs to be specified.
            // This may change in a future version of Windows. When that happens an OS build version check
            // can be added here to avoid WinHttpUnaryContent.
            return Channel.HttpHandlerType != HttpHandlerType.WinHttpHandler
                ? new PushUnaryContent<TRequest, TResponse>(request, WriteAsync)
                : new WinHttpUnaryContent<TRequest, TResponse>(request, WriteAsync, this);

            ValueTask WriteAsync(TRequest request, Stream stream)
            {
                return WriteMessageAsync(stream, request, Options);
            }
        }

        public void StartDuplexStreaming()
        {
            var clientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(this);
            var content = new PushStreamContent<TRequest, TResponse>(clientStreamWriter);

            StartDuplexStreamingCore(clientStreamWriter, content);
        }

        internal void StartUnaryCore(HttpContent content)
        {
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetMessageContent(content, message);
            _ = RunCall(message, timeout);
        }

        internal void StartServerStreamingCore(HttpContent content)
        {
            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetMessageContent(content, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message, timeout);
        }

        internal void StartClientStreamingCore(HttpContentClientStreamWriter<TRequest, TResponse> clientStreamWriter, HttpContent content)
        {
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetWriter(message, clientStreamWriter, content);
            _ = RunCall(message, timeout);
        }

        public void StartDuplexStreamingCore(HttpContentClientStreamWriter<TRequest, TResponse> clientStreamWriter, HttpContent content)
        {
            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetWriter(message, clientStreamWriter, content);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message, timeout);
        }

        public void Dispose()
        {
            using (StartScope())
            {
                Disposed = true;

                Cleanup(GrpcProtocolConstants.DisposeCanceledStatus);
            }
        }

        /// <summary>
        /// Clean up can be called by:
        /// 1. The user. AsyncUnaryCall.Dispose et al will call this on Dispose
        /// 2. <see cref="GrpcCall.ValidateHeaders"/> will call dispose if errors fail validation
        /// 3. <see cref="FinishResponseAndCleanUp"/> will call dispose
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

            Channel.FinishActiveCall(this);

            _ctsRegistration?.Dispose();

            if (_deadlineTimer != null)
            {
                lock (this)
                {
                    // Timer callback can call Timer.Change so dispose deadline timer in a lock
                    // and set to null to indicate to the callback that it has been disposed.
                    _deadlineTimer?.Dispose();
                    _deadlineTimer = null;
                }
            }

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
            var status = (CallTask.IsCompletedSuccessfully()) ? CallTask.Result : new Status(StatusCode.Cancelled, string.Empty);
            return CreateRpcException(status);
        }

        private void FinishResponseAndCleanUp(Status status)
        {
            ResponseFinished = true;

            // Clean up call resources once this call is finished
            // Call may not be explicitly disposed when used with unary methods
            // e.g. var reply = await client.SayHelloAsync(new HelloRequest());
            Cleanup(status);
        }

        /// <summary>
        /// Used by response stream reader to report it is finished.
        /// </summary>
        /// <param name="status">The completed response status code.</param>
        /// <param name="finishedGracefully">true when the end of the response stream was read, otherwise false.</param>
        public void ResponseStreamEnded(Status status, bool finishedGracefully)
        {
            if (finishedGracefully)
            {
                // Set response finished immediately rather than set it in logic resumed
                // from the callTcs to avoid race condition.
                // e.g. response stream finished and then immediately call GetTrailers().
                ResponseFinished = true;
            }

            _callTcs.TrySetResult(status);
        }

        public Task<Metadata> GetResponseHeadersAsync()
        {
            if (_responseHeadersTask == null)
            {
                // Allocate metadata and task only when requested
                _responseHeadersTask = GetResponseHeadersCoreAsync();
            }

            return _responseHeadersTask;
        }

        private async Task<Metadata> GetResponseHeadersCoreAsync()
        {
            try
            {
                var httpResponse = await HttpResponseTask.ConfigureAwait(false);

                // Check if the headers have a status. If they do then wait for the overall call task
                // to complete before returning headers. This means that if the call failed with a
                // a status then it is possible to await response headers and then call GetStatus().
                var grpcStatus = GrpcProtocolHelpers.GetHeaderValue(httpResponse.Headers, GrpcProtocolConstants.StatusTrailer);
                if (grpcStatus != null)
                {
                    await CallTask.ConfigureAwait(false);
                }

                var metadata = GrpcProtocolHelpers.BuildMetadata(httpResponse.Headers);

                // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#exposed-retry-metadata
                if (_attemptCount > 1)
                {
                    metadata.Add(GrpcProtocolConstants.RetryPreviousAttemptsHeader, (_attemptCount - 1).ToString(CultureInfo.InvariantCulture));
                }

                return metadata;
            }
            catch (Exception ex) when (ResolveException(ErrorStartingCallMessage, ex, out _, out var resolvedException))
            {
                throw resolvedException;
            }
        }

        public Status GetStatus()
        {
            using (StartScope())
            {
                if (CallTask.IsCompletedSuccessfully())
                {
                    return CallTask.Result;
                }

                throw new InvalidOperationException("Unable to get the status because the call is not complete.");
            }
        }

        public Task<TResponse> GetResponseAsync()
        {
            CompatibilityHelpers.Assert(_responseTcs != null);
            return _responseTcs.Task;
        }

        public Metadata GetTrailers()
        {
            using (StartScope())
            {
                if (!TryGetTrailers(out var trailers))
                {
                    // Throw InvalidOperationException here because documentation on GetTrailers says that
                    // InvalidOperationException is thrown if the call is not complete.
                    throw new InvalidOperationException("Can't get the call trailers because the call has not completed successfully.");
                }

                return trailers;
            }
        }

        private void SetMessageContent(HttpContent content, HttpRequestMessage message)
        {
            RequestGrpcEncoding = GrpcProtocolHelpers.GetRequestEncoding(message.Headers);
            message.Content = content;
        }

        public bool TryRegisterCancellation(
            CancellationToken cancellationToken,
            [NotNullWhen(true)] out CancellationTokenRegistration? cancellationTokenRegistration)
        {
            // Only register if the token:
            // 1. Can be canceled.
            // 2. The token isn't the same one used in CallOptions. Already listening for its cancellation.
            if (cancellationToken.CanBeCanceled && cancellationToken != Options.CancellationToken)
            {
                cancellationTokenRegistration = cancellationToken.Register(
                    static (state) => ((GrpcCall<TRequest, TResponse>)state!).CancelCallFromCancellationToken(),
                    this);
                return true;
            }

            cancellationTokenRegistration = null;
            return false;
        }

        private void CancelCallFromCancellationToken()
        {
            using (StartScope())
            {
                CancelCall(GrpcProtocolConstants.ClientCanceledStatus);
            }
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
                GrpcCallLog.CanceledCall(Logger);

                // Cancel in-progress HttpClient.SendAsync and Stream.ReadAsync tasks.
                // Cancel will send RST_STREAM if HttpClient.SendAsync isn't complete.
                // Cancellation will also cause reader/writer to throw if used afterwards.
                _callCts.Cancel();

                // Ensure any logic that is waiting on the HttpResponse is unstuck.
                _httpResponseTcs.TrySetCanceled();

                // Cancellation token won't send RST_STREAM if HttpClient.SendAsync is complete.
                // Dispose HttpResponseMessage to send RST_STREAM to server for in-progress calls.
                HttpResponse?.Dispose();

                // Canceling call will cancel pending writes to the stream
                ClientStreamWriter?.WriteStreamTcs.TrySetCanceled();
                ClientStreamWriter?.CompleteTcs.TrySetCanceled();
                ClientStreamReader?.HttpResponseTcs.TrySetCanceled();
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

        private async Task RunCall(HttpRequestMessage request, TimeSpan? timeout)
        {
            using (StartScope())
            {
                var (diagnosticSourceEnabled, activity) = InitializeCall(request, timeout);

                // Unset variable to check that FinishCall is called in every code path
                bool finished;

                Status? status = null;

                try
                {
                    // User supplied call credentials could error and so must be run
                    // inside try/catch to handle errors.
                    if (Options.Credentials != null || Channel.CallCredentials?.Count > 0)
                    {
                        await ReadCredentials(request).ConfigureAwait(false);
                    }

                    // Fail early if deadline has already been exceeded
                    _callCts.Token.ThrowIfCancellationRequested();

                    try
                    {
                        // If a HttpClient has been specified then we need to call it with ResponseHeadersRead
                        // so that the response message is available for streaming
                        var httpResponseTask = (Channel.HttpInvoker is HttpClient httpClient)
                            ? httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _callCts.Token)
                            : Channel.HttpInvoker.SendAsync(request, _callCts.Token);

                        HttpResponse = await httpResponseTask.ConfigureAwait(false);
                        _httpResponseTcs.TrySetResult(HttpResponse);
                    }
                    catch (Exception ex)
                    {
                        if (IsCancellationOrDeadlineException(ex))
                        {
                            throw;
                        }
                        else
                        {
                            GrpcCallLog.ErrorStartingCall(Logger, ex);
                            throw;
                        }
                    }

                    GrpcCallLog.ResponseHeadersReceived(Logger);
                    status = ValidateHeaders(HttpResponse, out var trailers);
                    if (trailers != null)
                    {
                        Trailers = trailers;
                    }

                    // A status means either the call has failed or grpc-status was returned in the response header
                    if (status != null)
                    {
                        if (_responseTcs != null)
                        {
                            // gRPC status in the header
                            if (status.Value.StatusCode != StatusCode.OK)
                            {
                                finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);
                            }
                            else
                            {
                                // The server should never return StatusCode.OK in the header for a unary call.
                                // If it does then throw an error that no message was returned from the server.
                                GrpcCallLog.MessageNotReturned(Logger);

                                // Change the status code to a more accurate status.
                                // This is consistent with Grpc.Core client behavior.
                                status = new Status(StatusCode.Internal, "Failed to deserialize response message.");

                                finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);
                            }

                            FinishResponseAndCleanUp(status.Value);

                            // Set failed result makes the response task thrown an error. Must be called after
                            // the response is finished. Reasons:
                            // - Finishing the response sets the status. Required for GetStatus to be successful.
                            // - We want GetStatus to always work when called after the response task is done.
                            SetFailedResult(status.Value);
                        }
                        else
                        {
                            finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);
                            FinishResponseAndCleanUp(status.Value);
                        }
                    }
                    else
                    {
                        if (_responseTcs != null)
                        {
                            // Read entire response body immediately and read status from trailers
                            // Trailers are only available once the response body had been read
                            var responseStream = await HttpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            var message = await ReadMessageAsync(
                                responseStream,
                                GrpcProtocolHelpers.GetGrpcEncoding(HttpResponse),
                                singleMessage: true,
                                _callCts.Token).ConfigureAwait(false);
                            status = GrpcProtocolHelpers.GetResponseStatus(HttpResponse, Channel.OperatingSystem.IsBrowser, Channel.HttpHandlerType == HttpHandlerType.WinHttpHandler);

                            if (message == null)
                            {
                                GrpcCallLog.MessageNotReturned(Logger);

                                if (status.Value.StatusCode == StatusCode.OK)
                                {
                                    // Change the status code if OK is returned to a more accurate status.
                                    // This is consistent with Grpc.Core client behavior.
                                    status = new Status(StatusCode.Internal, "Failed to deserialize response message.");
                                }

                                FinishResponseAndCleanUp(status.Value);
                                finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);

                                // Set failed result makes the response task thrown an error. Must be called after
                                // the response is finished. Reasons:
                                // - Finishing the response sets the status. Required for GetStatus to be successful.
                                // - We want GetStatus to always work when called after the response task is done.
                                SetFailedResult(status.Value);
                            }
                            else
                            {
                                GrpcEventSource.Log.MessageReceived();

                                FinishResponseAndCleanUp(status.Value);
                                finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);

                                if (status.Value.StatusCode == StatusCode.OK)
                                {
                                    _responseTcs.TrySetResult(message);
                                }
                                else
                                {
                                    // Set failed result makes the response task thrown an error. Must be called after
                                    // the response is finished. Reasons:
                                    // - Finishing the response sets the status. Required for GetStatus to be successful.
                                    // - We want GetStatus to always work when called after the response task is done.
                                    SetFailedResult(status.Value);
                                }
                            }
                        }
                        else
                        {
                            // Duplex or server streaming call
                            CompatibilityHelpers.Assert(ClientStreamReader != null);
                            ClientStreamReader.HttpResponseTcs.TrySetResult((HttpResponse, status));

                            // Wait until the response has been read and status read from trailers.
                            // TCS will also be set in Dispose.
                            status = await CallTask.ConfigureAwait(false);

                            finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);
                            Cleanup(status.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ResolveException(ErrorStartingCallMessage, ex, out status, out var resolvedException);

                    finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);

                    // Update HTTP response TCS before clean up. Needs to happen first because cleanup will
                    // cancel the TCS for anyone still listening.
                    _httpResponseTcs.TrySetException(resolvedException);

                    Cleanup(status.Value);

                    // Update response TCS after overall call status is resolved. This is required so that
                    // the call is completed before an error is thrown from ResponseAsync. If it happens
                    // afterwards then there is a chance GetStatus() will error because the call isn't complete.
                    _responseTcs?.TrySetException(resolvedException);
                }

                // Verify that FinishCall is called in every code path of this method.
                // Should create an "Unassigned variable" compiler error if not set.
                Debug.Assert(finished);
            }
        }

        private bool IsCancellationOrDeadlineException(Exception ex)
        {
            // Don't log OperationCanceledException if deadline has exceeded
            // or the call has been canceled.
            if (ex is OperationCanceledException &&
                _callTcs.Task.IsCompletedSuccessfully() &&
                (_callTcs.Task.Result.StatusCode == StatusCode.DeadlineExceeded || _callTcs.Task.Result.StatusCode == StatusCode.Cancelled))
            {
                return true;
            }

            // Exception may specify RST_STREAM or abort code that resolves to cancellation.
            // If protocol error is cancellation and deadline has been exceeded then that
            // means the server canceled and the local deadline timer hasn't triggered.
            if (GrpcProtocolHelpers.ResolveRpcExceptionStatusCode(ex) == StatusCode.Cancelled)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolve the specified exception to an end-user exception that will be thrown from the client.
        /// The resolved exception is normally a RpcException. Returns true when the resolved exception is changed.
        /// </summary>
        internal bool ResolveException(string summary, Exception ex, [NotNull] out Status? status, out Exception resolvedException)
        {
            if (ex is OperationCanceledException)
            {
                status = (CallTask.IsCompletedSuccessfully()) ? CallTask.Result : new Status(StatusCode.Cancelled, string.Empty);
                if (!Channel.ThrowOperationCanceledOnCancellation)
                {
                    resolvedException = CreateRpcException(status.Value);
                    return true;
                }
            }
            else if (ex is RpcException rpcException)
            {
                status = rpcException.Status;

                // If trailers have been set, and the RpcException isn't using them, then
                // create new RpcException with trailers. Want to try and avoid this as
                // the exact stack location will be lost.
                //
                // Trailers could be set in another thread so copy to local variable.
                var trailers = Trailers;
                if (trailers != null && rpcException.Trailers != trailers)
                {
                    resolvedException = CreateRpcException(status.Value);
                    return true;
                }
            }
            else
            {
                var s = GrpcProtocolHelpers.CreateStatusFromException(summary, ex);

                // The server could exceed the deadline and return a CANCELLED status before the
                // client's deadline timer is triggered. When CANCELLED is received check the
                // deadline against the clock and change status to DEADLINE_EXCEEDED if required.
                if (s.StatusCode == StatusCode.Cancelled)
                {
                    lock (this)
                    {
                        if (IsDeadlineExceededUnsynchronized())
                        {
                            s = new Status(StatusCode.DeadlineExceeded, s.Detail, s.DebugException);
                        }
                    }
                }

                status = s;
                resolvedException = CreateRpcException(s);
                return true;
            }

            resolvedException = ex;
            return false;
        }

        private void SetFailedResult(Status status)
        {
            CompatibilityHelpers.Assert(_responseTcs != null);

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
                _responseTcs.TrySetException(CreateRpcException(status));
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
                return CreateRpcException(status);
            }
        }

        private (bool diagnosticSourceEnabled, Activity? activity) InitializeCall(HttpRequestMessage request, TimeSpan? timeout)
        {
            GrpcCallLog.StartingCall(Logger, Method.Type, request.RequestUri!);
            GrpcEventSource.Log.CallStart(Method.FullName);

            // Deadline will cancel the call CTS.
            // Only exceed deadline/start timer after reader/writer have been created, otherwise deadline will cancel
            // the call CTS before they are created and leave them in a non-canceled state.
            if (timeout != null && !Channel.DisableClientDeadline)
            {
                if (timeout.Value <= TimeSpan.Zero)
                {
                    // Call was started with a deadline in the past so immediately trigger deadline exceeded.
                    DeadlineExceeded();
                }
                else
                {
                    GrpcCallLog.StartingDeadlineTimeout(Logger, timeout.Value);

                    var dueTime = CommonGrpcProtocolHelpers.GetTimerDueTime(timeout.Value, Channel.MaxTimerDueTime);
                    _deadlineTimer = new Timer(DeadlineExceededCallback, null, dueTime, Timeout.Infinite);
                }
            }

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
                    WriteDiagnosticEvent(GrpcDiagnostics.DiagnosticListener, GrpcDiagnostics.ActivityStartKey, new ActivityStartData(request));
                }
            }

            if (Options.CancellationToken.CanBeCanceled)
            {
                // The cancellation token will cancel the call CTS.
                // This must be registered after the client writer has been created
                // so that cancellation will always complete the writer.
                _ctsRegistration = Options.CancellationToken.Register(
                    static (state) => ((GrpcCall<TRequest, TResponse>)state!).CancelCallFromCancellationToken(),
                    this);
            }

            return (diagnosticSourceEnabled, activity);
        }

        private bool FinishCall(HttpRequestMessage request, bool diagnosticSourceEnabled, Activity? activity, Status status)
        {
            if (status.StatusCode != StatusCode.OK)
            {
                if (status.StatusCode == StatusCode.DeadlineExceeded)
                {
                    // Usually a deadline will be triggered via the deadline timer. However,
                    // if the client and server are on the same machine it is possible for the
                    // client to get the response before the timer triggers. In that situation
                    // treat a returned DEADLINE_EXCEEDED status as the client exceeding deadline.
                    // To ensure that the deadline counter isn't incremented twice in a race
                    // between the timer and status, lock and use _deadline to check whether
                    // the client has processed that it has exceeded or not.
                    lock (this)
                    {
                        if (IsDeadlineExceededUnsynchronized())
                        {
                            GrpcCallLog.DeadlineExceeded(Logger);
                            GrpcEventSource.Log.CallDeadlineExceeded();

                            _deadline = DateTime.MaxValue;
                        }
                    }
                }

                GrpcCallLog.GrpcStatusError(Logger, status.StatusCode, status.Detail);
                GrpcEventSource.Log.CallFailed(status.StatusCode);
            }
            GrpcCallLog.FinishedCall(Logger);
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

                    WriteDiagnosticEvent(GrpcDiagnostics.DiagnosticListener, GrpcDiagnostics.ActivityStopKey, new ActivityStopData(HttpResponse, request));
                }

                activity.Stop();
            }

            return true;
        }

        private bool IsDeadlineExceededUnsynchronized()
        {
            Debug.Assert(Monitor.IsEntered(this), "Check deadline in a lock. Updating a DateTime isn't guaranteed to be atomic. Avoid struct tearing.");
            return _deadline <= Channel.Clock.UtcNow;
        }

        private async Task ReadCredentials(HttpRequestMessage request)
        {
            // In C-Core the call credential auth metadata is only applied if the channel is secure
            // The equivalent in grpc-dotnet is only applying metadata if HttpClient is using TLS
            // HttpClient scheme will be HTTP if it is using H2C (HTTP2 without TLS)
            if (Channel.IsSecure)
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
                GrpcCallLog.CallCredentialsNotUsed(Logger);
            }
        }

        private void SetWriter(HttpRequestMessage message, HttpContentClientStreamWriter<TRequest, TResponse> clientStreamWriter, HttpContent content)
        {
            RequestGrpcEncoding = GrpcProtocolHelpers.GetRequestEncoding(message.Headers);
            ClientStreamWriter = clientStreamWriter;
            message.Content = content;
        }

        private HttpRequestMessage CreateHttpRequestMessage(TimeSpan? timeout)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _grpcMethodInfo.CallUri);
            message.Version = GrpcProtocolConstants.Http2Version;
#if NET5_0_OR_GREATER
            message.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
#endif

            // Set raw headers on request using name/values. Typed headers allocate additional objects.
            var headers = message.Headers;

            // User agent is optional but recommended.
            headers.TryAddWithoutValidation(GrpcProtocolConstants.UserAgentHeader, GrpcProtocolConstants.UserAgentHeaderValue);
            // TE is required by some servers, e.g. C Core.
            // A missing TE header results in servers aborting the gRPC call.
            headers.TryAddWithoutValidation(GrpcProtocolConstants.TEHeader, GrpcProtocolConstants.TEHeaderValue);
            headers.TryAddWithoutValidation(GrpcProtocolConstants.MessageAcceptEncodingHeader, Channel.MessageAcceptEncoding);

            // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#exposed-retry-metadata
            if (_attemptCount > 1)
            {
                headers.TryAddWithoutValidation(GrpcProtocolConstants.RetryPreviousAttemptsHeader, (_attemptCount - 1).ToString(CultureInfo.InvariantCulture));
            }

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
                        headers.TryAddWithoutValidation(GrpcProtocolConstants.MessageEncodingHeader, entry.Value);
                    }
                    else
                    {
                        GrpcProtocolHelpers.AddHeader(headers, entry);
                    }
                }
            }

            if (timeout != null)
            {
                headers.TryAddWithoutValidation(GrpcProtocolConstants.TimeoutHeader, GrpcProtocolHelpers.EncodeTimeout(timeout.Value.Ticks / TimeSpan.TicksPerMillisecond));
            }

#if SUPPORT_LOAD_BALANCING
            if (Options.IsWaitForReady)
            {
                message.SetOption(BalancerHttpHandler.WaitForReadyKey, true);
            }
#endif

            return message;
        }

        private TimeSpan? GetTimeout()
        {
            if (_deadline == DateTime.MaxValue)
            {
                return null;
            }

            var timeout = _deadline - Channel.Clock.UtcNow;

            // Maxmimum deadline of 99999999s is consistent with Grpc.Core
            // https://github.com/grpc/grpc/blob/907a1313a87723774bf59d04ed432602428245c3/src/core/lib/transport/timeout_encoding.h#L32-L34
            const long MaxDeadlineTicks = 99999999 * TimeSpan.TicksPerSecond;

            if (timeout.Ticks > MaxDeadlineTicks)
            {
                GrpcCallLog.DeadlineTimeoutTooLong(Logger, timeout);

                timeout = TimeSpan.FromTicks(MaxDeadlineTicks);
            }

            return timeout;
        }

        private void DeadlineExceededCallback(object? state)
        {
            // Deadline is only exceeded if the timeout has passed and
            // the response has not been finished or canceled
            if (!_callCts.IsCancellationRequested && !ResponseFinished)
            {
                TimeSpan remaining;
                lock (this)
                {
                    // If _deadline is MaxValue then the DEADLINE_EXCEEDED status has
                    // already been received by the client and the timer can stop.
                    if (_deadline == DateTime.MaxValue)
                    {
                        return;
                    }

                    remaining = _deadline - Channel.Clock.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        DeadlineExceeded();
                        return;
                    }

                    if (_deadlineTimer != null)
                    {
                        // Deadline has not been reached because timer maximum due time was smaller than deadline.
                        // Reschedule DeadlineExceeded again until deadline has been exceeded.
                        GrpcCallLog.DeadlineTimerRescheduled(Logger, remaining);

                        var dueTime = CommonGrpcProtocolHelpers.GetTimerDueTime(remaining, Channel.MaxTimerDueTime);
                        _deadlineTimer.Change(dueTime, Timeout.Infinite);
                    }
                }
            }
        }

        private void DeadlineExceeded()
        {
            GrpcCallLog.DeadlineExceeded(Logger);
            GrpcEventSource.Log.CallDeadlineExceeded();

            CancelCall(new Status(StatusCode.DeadlineExceeded, string.Empty));
        }

        internal ValueTask WriteMessageAsync(
            Stream stream,
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken)
        {
            MessagesWritten++;
            return stream.WriteMessageAsync(
                this,
                message,
                cancellationToken);
        }

        internal ValueTask WriteMessageAsync(
            Stream stream,
            TRequest message,
            CallOptions callOptions)
        {
            MessagesWritten++;
            return stream.WriteMessageAsync(
                this,
                message,
                Method.RequestMarshaller.ContextualSerializer,
                callOptions);
        }

#if !NETSTANDARD2_0
        internal ValueTask<TResponse?> ReadMessageAsync(
#else
        internal Task<TResponse?> ReadMessageAsync(
#endif
            Stream responseStream,
            string grpcEncoding,
            bool singleMessage,
            CancellationToken cancellationToken)
        {
            return responseStream.ReadMessageAsync(
                this,
                Method.ResponseMarshaller.ContextualDeserializer,
                grpcEncoding,
                singleMessage,
                cancellationToken);
        }

        public Task WriteClientStreamAsync<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, ValueTask> writeFunc, TState state)
        {
            return ClientStreamWriter!.WriteAsync(writeFunc, state);
        }

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
            Justification = "The values being passed into Write have the commonly used properties being preserved with DynamicDependency.")]
#endif
        private static void WriteDiagnosticEvent<
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
#endif
            TValue>(
            DiagnosticSource diagnosticSource,
            string name,
            TValue value)
        {
            diagnosticSource.Write(name, value);
        }
    }
}
