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
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Internal.Http;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

#if NETSTANDARD2_0
using ValueTask = System.Threading.Tasks.Task;
#endif

namespace Grpc.Net.Client.Internal
{
    internal sealed partial class GrpcCall<TRequest, TResponse> : GrpcCall, IDisposable
        where TRequest : class
        where TResponse : class
    {
        private const string ErrorStartingCallMessage = "Error starting gRPC call.";

        private readonly CancellationTokenSource _callCts;
        private readonly TaskCompletionSource<Status> _callTcs;
        private readonly DateTime _deadline;
        private readonly GrpcMethodInfo _grpcMethodInfo;

        private Task<HttpResponseMessage>? _httpResponseTask;
        private Task<Metadata>? _responseHeadersTask;
        private Timer? _deadlineTimer;
        private CancellationTokenRegistration? _ctsRegistration;

        public bool Disposed { get; private set; }
        public Method<TRequest, TResponse> Method { get; }

        // These are set depending on the type of gRPC call
        private TaskCompletionSource<TResponse>? _responseTcs;
        public HttpContentClientStreamWriter<TRequest, TResponse>? ClientStreamWriter { get; private set; }
        public HttpContentClientStreamReader<TRequest, TResponse>? ClientStreamReader { get; private set; }

        public GrpcCall(Method<TRequest, TResponse> method, GrpcMethodInfo grpcMethodInfo, CallOptions options, GrpcChannel channel)
            : base(options, channel)
        {
            // Validate deadline before creating any objects that require cleanup
            ValidateDeadline(options.Deadline);

            _callCts = new CancellationTokenSource();
            // Run the callTcs continuation immediately to keep the same context. Required for Activity.
            _callTcs = new TaskCompletionSource<Status>();
            Method = method;
            _grpcMethodInfo = grpcMethodInfo;
            _deadline = options.Deadline ?? DateTime.MaxValue;

            Channel.RegisterActiveCall(this);
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

        public override Type RequestType => typeof(TRequest);
        public override Type ResponseType => typeof(TResponse);

        public void StartUnary(TRequest request)
        {
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetMessageContent(request, message);
            _ = RunCall(message, timeout);
        }

        public void StartClientStreaming()
        {
            _responseTcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            CreateWriter(message);
            _ = RunCall(message, timeout);
        }

        public void StartServerStreaming(TRequest request)
        {
            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            SetMessageContent(request, message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message, timeout);
        }

        public void StartDuplexStreaming()
        {
            var timeout = GetTimeout();
            var message = CreateHttpRequestMessage(timeout);
            CreateWriter(message);
            ClientStreamReader = new HttpContentClientStreamReader<TRequest, TResponse>(this);
            _ = RunCall(message, timeout);
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
        public void ResponseStreamEnded(Status status)
        {
            // Set response finished immediately rather than set it in logic resumed
            // from the callTcs to avoid race condition.
            // e.g. response stream finished and then immediately call GetTrailers().
            ResponseFinished = true;

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
            CompatibilityExtensions.Assert(_httpResponseTask != null);

            try
            {
                var httpResponse = await _httpResponseTask.ConfigureAwait(false);

                // Check if the headers have a status. If they do then wait for the overall call task
                // to complete before returning headers. This means that if the call failed with a
                // a status then it is possible to await response headers and then call GetStatus().
                var grpcStatus = GrpcProtocolHelpers.GetHeaderValue(httpResponse.Headers, GrpcProtocolConstants.StatusTrailer);
                if (grpcStatus != null)
                {
                    await CallTask.ConfigureAwait(false);
                }

                return GrpcProtocolHelpers.BuildMetadata(httpResponse.Headers);
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
            CompatibilityExtensions.Assert(_responseTcs != null);
            return _responseTcs.Task;
        }

        private Status? ValidateHeaders(HttpResponseMessage httpResponse)
        {
            GrpcCallLog.ResponseHeadersReceived(Logger);

            // gRPC status can be returned in the header when there is no message (e.g. unimplemented status)
            // An explicitly specified status header has priority over other failing statuses
            if (GrpcProtocolHelpers.TryGetStatusCore(httpResponse.Headers, out var status))
            {
                // Trailers are in the header because there is no message.
                // Note that some default headers will end up in the trailers (e.g. Date, Server).
                Trailers = GrpcProtocolHelpers.BuildMetadata(httpResponse.Headers);
                return status;
            }

            // ALPN negotiation is sending HTTP/1.1 and HTTP/2.
            // Check that the response wasn't downgraded to HTTP/1.1.
            if (httpResponse.Version < CompatibilityExtensions.Version20)
            {
                return new Status(StatusCode.Internal, $"Bad gRPC response. Response protocol downgraded to HTTP/{httpResponse.Version.ToString(2)}.");
            }

            if (httpResponse.StatusCode != HttpStatusCode.OK)
            {
                var statusCode = MapHttpStatusToGrpcCode(httpResponse.StatusCode);
                return new Status(statusCode, "Bad gRPC response. HTTP status code: " + (int)httpResponse.StatusCode);
            }
            
            if (httpResponse.Content?.Headers.ContentType == null)
            {
                return new Status(StatusCode.Cancelled, "Bad gRPC response. Response did not have a content-type header.");
            }

            var grpcEncoding = httpResponse.Content.Headers.ContentType;
            if (!CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, grpcEncoding?.MediaType))
            {
                return new Status(StatusCode.Cancelled, "Bad gRPC response. Invalid content-type value: " + grpcEncoding);
            }

            // Call is still in progress
            return null;
        }

        private static StatusCode MapHttpStatusToGrpcCode(HttpStatusCode httpStatusCode)
        {
            switch (httpStatusCode)
            {
                case HttpStatusCode.BadRequest:  // 400
#if !NETSTANDARD2_0
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
#if !NETSTANDARD2_0
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

        private void SetMessageContent(TRequest request, HttpRequestMessage message)
        {
            RequestGrpcEncoding = GrpcProtocolHelpers.GetRequestEncoding(message.Headers);

            if (!Channel.IsWinHttp)
            {
                message.Content = new PushUnaryContent<TRequest, TResponse>(
                    request,
                    this,
                    GrpcProtocolConstants.GrpcContentTypeHeaderValue);
            }
            else
            {
                // WinHttp doesn't support streaming request data so a length needs to be specified.
                message.Content = new LengthUnaryContent<TRequest, TResponse>(
                    request,
                    this,
                    GrpcProtocolConstants.GrpcContentTypeHeaderValue);
            }
        }

        public void CancelCallFromCancellationToken()
        {
            using (StartScope())
            {
                CancelCall(new Status(StatusCode.Cancelled, "Call canceled by the client."));
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

                if (Options.Credentials != null || Channel.CallCredentials?.Count > 0)
                {
                    await ReadCredentials(request).ConfigureAwait(false);
                }

                // Unset variable to check that FinishCall is called in every code path
                bool finished;

                Status? status = null;

                try
                {
                    // Fail early if deadline has already been exceeded
                    _callCts.Token.ThrowIfCancellationRequested();

                    try
                    {
                        // If a HttpClient has been specified then we need to call it with ResponseHeadersRead
                        // so that the response message is available for streaming
                        _httpResponseTask = (Channel.HttpInvoker is HttpClient httpClient)
                            ? httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _callCts.Token)
                            : Channel.HttpInvoker.SendAsync(request, _callCts.Token);

                        HttpResponse = await _httpResponseTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        GrpcCallLog.ErrorStartingCall(Logger, ex);
                        throw;
                    }

                    status = ValidateHeaders(HttpResponse);

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
                            status = GrpcProtocolHelpers.GetResponseStatus(HttpResponse, Channel.OperatingSystem.IsBrowser);

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
                            CompatibilityExtensions.Assert(ClientStreamReader != null);
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
                    Exception resolvedException;
                    ResolveException(ErrorStartingCallMessage, ex, out status, out resolvedException);

                    finished = FinishCall(request, diagnosticSourceEnabled, activity, status.Value);
                    _responseTcs?.TrySetException(resolvedException);

                    Cleanup(status.Value);
                }

                // Verify that FinishCall is called in every code path of this method.
                // Should create an "Unassigned variable" compiler error if not set.
                Debug.Assert(finished);
            }
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
                var exceptionMessage = CommonGrpcProtocolHelpers.ConvertToRpcExceptionMessage(ex);
                var statusCode = GrpcProtocolHelpers.ResolveRpcExceptionStatusCode(ex);

                status = new Status(statusCode, summary + " " + exceptionMessage, ex);
                resolvedException = CreateRpcException(status.Value);
                return true;
            }

            resolvedException = ex;
            return false;
        }

        private void SetFailedResult(Status status)
        {
            CompatibilityExtensions.Assert(_responseTcs != null);

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

                    var dueTime = GetTimerDueTime(timeout.Value);
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
                    GrpcDiagnostics.DiagnosticListener.Write(GrpcDiagnostics.ActivityStartKey, new { Request = request });
                }
            }

            if (Options.CancellationToken.CanBeCanceled)
            {
                // The cancellation token will cancel the call CTS.
                // This must be registered after the client writer has been created
                // so that cancellation will always complete the writer.
                _ctsRegistration = Options.CancellationToken.Register(CancelCallFromCancellationToken);
            }

            return (diagnosticSourceEnabled, activity);
        }

        private bool FinishCall(HttpRequestMessage request, bool diagnosticSourceEnabled, Activity? activity, Status? status)
        {
            if (status!.Value.StatusCode != StatusCode.OK)
            {
                GrpcCallLog.GrpcStatusError(Logger, status.Value.StatusCode, status.Value.Detail);
                GrpcEventSource.Log.CallFailed(status.Value.StatusCode);
            }
            GrpcCallLog.FinishedCall(Logger);
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

            return true;
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
                GrpcCallLog.CallCredentialsNotUsed(Logger);
            }
        }

        private void CreateWriter(HttpRequestMessage message)
        {
            RequestGrpcEncoding = GrpcProtocolHelpers.GetRequestEncoding(message.Headers);
            ClientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(this);

            message.Content = new PushStreamContent<TRequest, TResponse>(ClientStreamWriter, GrpcProtocolConstants.GrpcContentTypeHeaderValue);
        }

        private HttpRequestMessage CreateHttpRequestMessage(TimeSpan? timeout)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _grpcMethodInfo.CallUri);
            message.Version = CompatibilityExtensions.Version20;
#if NET5_0
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

            return message;
        }

        private long GetTimerDueTime(TimeSpan timeout)
        {
            // Timer has a maximum allowed due time.
            // The called method will rechedule the timer if the deadline time has not passed.
            var dueTimeMilliseconds = timeout.Ticks / TimeSpan.TicksPerMillisecond;
            dueTimeMilliseconds = Math.Min(dueTimeMilliseconds, Channel.MaxTimerDueTime);
            // Timer can't have a negative due time
            dueTimeMilliseconds = Math.Max(dueTimeMilliseconds, 0);

            return dueTimeMilliseconds;
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
                var remaining = _deadline - Channel.Clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    DeadlineExceeded();
                }
                else
                {
                    // Deadline has not been reached because timer maximum due time was smaller than deadline.
                    // Reschedule DeadlineExceeded again until deadline has been exceeded.
                    GrpcCallLog.DeadlineTimerRescheduled(Logger, remaining);

                    _deadlineTimer!.Change(GetTimerDueTime(remaining), Timeout.Infinite);
                }
            }
        }

        private void DeadlineExceeded()
        {
            GrpcCallLog.DeadlineExceeded(Logger);
            GrpcEventSource.Log.CallDeadlineExceeded();

            CancelCall(new Status(StatusCode.DeadlineExceeded, string.Empty));
        }

        internal async ValueTask WriteMessageAsync(
            Stream stream,
            TRequest message,
            Action<TRequest, SerializationContext> contextualSerializer,
            CallOptions callOptions)
        {
            var serializationContext = SerializationContext;
            serializationContext.CallOptions = callOptions;
            serializationContext.Initialize();

            try
            {
                await stream.WriteMessageAsync(
                    this,
                    message,
                    contextualSerializer,
                    callOptions,
                    serializationContext).ConfigureAwait(false);
            }
            finally
            {
                serializationContext.Reset();
            }
        }

        internal ValueTask WriteMessageAsync<TSerializationContext>(
            Stream stream,
            TRequest message,
            Action<TRequest, SerializationContext> contextualSerializer,
            CallOptions callOptions,
            TSerializationContext serializationContext) where TSerializationContext : SerializationContext, IMemoryOwner<byte>
        {
            return stream.WriteMessageAsync(
                this,
                message,
                contextualSerializer,
                callOptions,
                serializationContext);
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
    }
}
