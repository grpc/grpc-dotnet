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

#if NET5_0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

// Copied with permission from https://github.com/dotnet/runtime/blob/7565d60891e43415f5e81b59e50c52dba46ee0d7/src/libraries/System.Net.Http/src/System/Net/Http/DiagnosticsHandler.cs
namespace Grpc.Shared
{
    /// <summary>
    /// This handler:
    /// 1. Propagates trace headers.
    /// 2. Starts and stops System.Net.Http.HttpRequestOut activity.
    /// 3. Writes to diagnostics listener.
    /// 
    /// These actions are required for OpenTelemetry and for AppInsights to detect HTTP requests.
    /// Note: Deprecated diagnostics listener events are still used by AppInsights.
    /// 
    /// Usually this logic is handled by https://github.com/dotnet/runtime/blob/7565d60891e43415f5e81b59e50c52dba46ee0d7/src/libraries/System.Net.Http/src/System/Net/Http/DiagnosticsHandler.cs.
    /// DiagnosticsHandler is only run when HttpClientHandler is used.
    /// If SocketsHttpHandler is used directly then this handler is added as a subsitute.
    /// </summary>
    internal sealed class TelemetryHeaderHandler : DelegatingHandler
    {
        public TelemetryHeaderHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Activity.Current != null || DiagnosticListener.IsEnabled())
            {
                return SendAsyncCore(request, cancellationToken);
            }

            return base.SendAsync(request, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendAsyncCore(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Activity? activity = null;
            var diagnosticListener = DiagnosticListener;

            // if there is no listener, but propagation is enabled (with previous IsEnabled() check)
            // do not write any events just start/stop Activity and propagate Ids
            if (!diagnosticListener.IsEnabled())
            {
                activity = new Activity(ActivityName);
                activity.Start();
                InjectHeaders(activity, request);

                try
                {
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    activity.Stop();
                }
            }

            var loggingRequestId = Guid.Empty;

            // There is a listener. Check if listener wants to be notified about HttpClient Activities
            if (diagnosticListener.IsEnabled(ActivityName, request))
            {
                activity = new Activity(ActivityName);

                // Only send start event to users who subscribed for it, but start activity anyway
                if (diagnosticListener.IsEnabled(ActivityStartName))
                {
                    diagnosticListener.StartActivity(activity, new ActivityStartData(request));
                }
                else
                {
                    activity.Start();
                }
            }
            // try to write System.Net.Http.Request event (deprecated)
            if (diagnosticListener.IsEnabled(RequestWriteNameDeprecated))
            {
                var timestamp = Stopwatch.GetTimestamp();
                loggingRequestId = Guid.NewGuid();
                diagnosticListener.Write(RequestWriteNameDeprecated, new RequestData(request, loggingRequestId, timestamp));
            }

            // If we are on at all, we propagate current activity information
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                InjectHeaders(currentActivity, request);
            }

            HttpResponseMessage? response = null;
            var taskStatus = TaskStatus.RanToCompletion;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (OperationCanceledException)
            {
                taskStatus = TaskStatus.Canceled;

                // we'll report task status in HttpRequestOut.Stop
                throw;
            }
            catch (Exception ex)
            {
                taskStatus = TaskStatus.Faulted;

                if (diagnosticListener.IsEnabled(ExceptionEventName))
                {
                    // If request was initially instrumented, Activity.Current has all necessary context for logging
                    // Request is passed to provide some context if instrumentation was disabled and to avoid
                    // extensive Activity.Tags usage to tunnel request properties
                    diagnosticListener.Write(ExceptionEventName, new ExceptionData(ex, request));
                }
                throw;
            }
            finally
            {
                // always stop activity if it was started
                if (activity != null)
                {
                    diagnosticListener.StopActivity(activity, new ActivityStopData(
                        response,
                        // If request is failed or cancelled, there is no response, therefore no information about request;
                        // pass the request in the payload, so consumers can have it in Stop for failed/canceled requests
                        // and not retain all requests in Start
                        request,
                        taskStatus));
                }
                // Try to write System.Net.Http.Response event (deprecated)
                if (diagnosticListener.IsEnabled(ResponseWriteNameDeprecated))
                {
                    var timestamp = Stopwatch.GetTimestamp();
                    diagnosticListener.Write(ResponseWriteNameDeprecated,
                        new ResponseData(
                            response,
                            loggingRequestId,
                            timestamp,
                            taskStatus));
                }
            }
        }

        public static readonly DiagnosticListener DiagnosticListener = new DiagnosticListener(DiagnosticListenerName);

        private const string DiagnosticListenerName = "HttpHandlerDiagnosticListener";
        private const string RequestWriteNameDeprecated = "System.Net.Http.Request";
        private const string ResponseWriteNameDeprecated = "System.Net.Http.Response";

        private const string ExceptionEventName = "System.Net.Http.Exception";
        private const string ActivityName = "System.Net.Http.HttpRequestOut";
        private const string ActivityStartName = "System.Net.Http.HttpRequestOut.Start";

        private const string RequestIdHeaderName = "Request-Id";
        private const string CorrelationContextHeaderName = "Correlation-Context";

        private const string TraceParentHeaderName = "traceparent";
        private const string TraceStateHeaderName = "tracestate";

        private sealed class ActivityStartData
        {
            internal ActivityStartData(HttpRequestMessage request)
            {
                Request = request;
            }

            public HttpRequestMessage Request { get; }

            public override string ToString() => $"{{ {nameof(Request)} = {Request} }}";
        }

        private sealed class ActivityStopData
        {
            internal ActivityStopData(HttpResponseMessage? response, HttpRequestMessage request, TaskStatus requestTaskStatus)
            {
                Response = response;
                Request = request;
                RequestTaskStatus = requestTaskStatus;
            }

            public HttpResponseMessage? Response { get; }
            public HttpRequestMessage Request { get; }
            public TaskStatus RequestTaskStatus { get; }

            public override string ToString() => $"{{ {nameof(Response)} = {Response}, {nameof(Request)} = {Request}, {nameof(RequestTaskStatus)} = {RequestTaskStatus} }}";
        }

        private sealed class ExceptionData
        {
            internal ExceptionData(Exception exception, HttpRequestMessage request)
            {
                Exception = exception;
                Request = request;
            }

            public Exception Exception { get; }
            public HttpRequestMessage Request { get; }

            public override string ToString() => $"{{ {nameof(Exception)} = {Exception}, {nameof(Request)} = {Request} }}";
        }

        private sealed class RequestData
        {
            internal RequestData(HttpRequestMessage request, Guid loggingRequestId, long timestamp)
            {
                Request = request;
                LoggingRequestId = loggingRequestId;
                Timestamp = timestamp;
            }

            public HttpRequestMessage Request { get; }
            public Guid LoggingRequestId { get; }
            public long Timestamp { get; }

            public override string ToString() => $"{{ {nameof(Request)} = {Request}, {nameof(LoggingRequestId)} = {LoggingRequestId}, {nameof(Timestamp)} = {Timestamp} }}";
        }

        private sealed class ResponseData
        {
            internal ResponseData(HttpResponseMessage? response, Guid loggingRequestId, long timestamp, TaskStatus requestTaskStatus)
            {
                Response = response;
                LoggingRequestId = loggingRequestId;
                Timestamp = timestamp;
                RequestTaskStatus = requestTaskStatus;
            }

            public HttpResponseMessage? Response { get; }
            public Guid LoggingRequestId { get; }
            public long Timestamp { get; }
            public TaskStatus RequestTaskStatus { get; }

            public override string ToString() => $"{{ {nameof(Response)} = {Response}, {nameof(LoggingRequestId)} = {LoggingRequestId}, {nameof(Timestamp)} = {Timestamp}, {nameof(RequestTaskStatus)} = {RequestTaskStatus} }}";
        }

        private static void InjectHeaders(Activity currentActivity, HttpRequestMessage request)
        {
            if (currentActivity.IdFormat == ActivityIdFormat.W3C)
            {
                if (!request.Headers.Contains(TraceParentHeaderName))
                {
                    request.Headers.TryAddWithoutValidation(TraceParentHeaderName, currentActivity.Id);
                    if (currentActivity.TraceStateString != null)
                    {
                        request.Headers.TryAddWithoutValidation(TraceStateHeaderName, currentActivity.TraceStateString);
                    }
                }
            }
            else
            {
                if (!request.Headers.Contains(RequestIdHeaderName))
                {
                    request.Headers.TryAddWithoutValidation(RequestIdHeaderName, currentActivity.Id);
                }
            }

            // we expect baggage to be empty or contain a few items
            using (IEnumerator<KeyValuePair<string, string?>> e = currentActivity.Baggage.GetEnumerator())
            {
                if (e.MoveNext())
                {
                    var baggage = new List<string>();
                    do
                    {
                        KeyValuePair<string, string?> item = e.Current;
                        baggage.Add(new NameValueHeaderValue(WebUtility.UrlEncode(item.Key), WebUtility.UrlEncode(item.Value)).ToString());
                    }
                    while (e.MoveNext());
                    request.Headers.TryAddWithoutValidation(CorrelationContextHeaderName, baggage);
                }
            }
        }
    }
}
#endif