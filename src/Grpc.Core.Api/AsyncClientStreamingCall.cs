#region Copyright notice and license

// Copyright 2015 gRPC authors.
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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Grpc.Core.Internal;

namespace Grpc.Core;

/// <summary>
/// Return type for client streaming calls.
/// </summary>
/// <typeparam name="TRequest">Request message type for this call.</typeparam>
/// <typeparam name="TResponse">Response message type for this call.</typeparam>
[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(AsyncClientStreamingCall<,>.AsyncClientStreamingCallDebugView))]
public sealed class AsyncClientStreamingCall<TRequest, TResponse> : IDisposable
{
    readonly IClientStreamWriter<TRequest> requestStream;
    readonly Task<TResponse> responseAsync;
    readonly AsyncCallState callState;

    /// <summary>
    /// Creates a new AsyncClientStreamingCall object with the specified properties.
    /// </summary>
    /// <param name="requestStream">Stream of request values.</param>
    /// <param name="responseAsync">The response of the asynchronous call.</param>
    /// <param name="responseHeadersAsync">Response headers of the asynchronous call.</param>
    /// <param name="getStatusFunc">Delegate returning the status of the call.</param>
    /// <param name="getTrailersFunc">Delegate returning the trailing metadata of the call.</param>
    /// <param name="disposeAction">Delegate to invoke when Dispose is called on the call object.</param>
    public AsyncClientStreamingCall(IClientStreamWriter<TRequest> requestStream,
                                    Task<TResponse> responseAsync,
                                    Task<Metadata> responseHeadersAsync,
                                    Func<Status> getStatusFunc,
                                    Func<Metadata> getTrailersFunc,
                                    Action disposeAction)
    {
        this.requestStream = requestStream;
        this.responseAsync = responseAsync;
        this.callState = new AsyncCallState(responseHeadersAsync, getStatusFunc, getTrailersFunc, disposeAction);
    }

    /// <summary>
    /// Creates a new AsyncClientStreamingCall object with the specified properties.
    /// </summary>
    /// <param name="requestStream">Stream of request values.</param>
    /// <param name="responseAsync">The response of the asynchronous call.</param>
    /// <param name="responseHeadersAsync">Response headers of the asynchronous call.</param>
    /// <param name="getStatusFunc">Delegate returning the status of the call.</param>
    /// <param name="getTrailersFunc">Delegate returning the trailing metadata of the call.</param>
    /// <param name="disposeAction">Delegate to invoke when Dispose is called on the call object.</param>
    /// <param name="state">State object for use with the callback parameters.</param>
    public AsyncClientStreamingCall(IClientStreamWriter<TRequest> requestStream,
                                    Task<TResponse> responseAsync,
                                    Func<object, Task<Metadata>> responseHeadersAsync,
                                    Func<object, Status> getStatusFunc,
                                    Func<object, Metadata> getTrailersFunc,
                                    Action<object> disposeAction,
                                    object state)
    {
        this.requestStream = requestStream;
        this.responseAsync = responseAsync;
        this.callState = new AsyncCallState(responseHeadersAsync, getStatusFunc, getTrailersFunc, disposeAction, state);
    }

    /// <summary>
    /// Asynchronous call result.
    /// </summary>
    public Task<TResponse> ResponseAsync
    {
        get
        {
            return this.responseAsync;
        }
    }

    /// <summary>
    /// Asynchronous access to response headers.
    /// </summary>
    public Task<Metadata> ResponseHeadersAsync
    {
        get
        {
            return callState.ResponseHeadersAsync();
        }
    }

    /// <summary>
    /// Async stream to send streaming requests.
    /// </summary>
    public IClientStreamWriter<TRequest> RequestStream
    {
        get
        {
            return requestStream;
        }
    }

    /// <summary>
    /// Gets an awaiter used to await this <see cref="AsyncClientStreamingCall{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>An awaiter instance.</returns>
    /// <remarks>This method is intended for compiler use rather than use directly in code.</remarks>
    public TaskAwaiter<TResponse> GetAwaiter()
    {
        return responseAsync.GetAwaiter();
    }

    /// <summary>
    /// Configures an awaiter used to await this <see cref="AsyncClientStreamingCall{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="continueOnCapturedContext">
    /// true to attempt to marshal the continuation back to the original context captured; otherwise, false.
    /// </param>
    /// <returns>An object used to await this task.</returns>
    public ConfiguredTaskAwaitable<TResponse> ConfigureAwait(bool continueOnCapturedContext)
    {
        return responseAsync.ConfigureAwait(continueOnCapturedContext);
    }

    /// <summary>
    /// Gets the call status if the call has already finished.
    /// Throws InvalidOperationException otherwise.
    /// </summary>
    public Status GetStatus()
    {
        return callState.GetStatus();
    }

    /// <summary>
    /// Gets the call trailing metadata if the call has already finished.
    /// Throws InvalidOperationException otherwise.
    /// </summary>
    public Metadata GetTrailers()
    {
        return callState.GetTrailers();
    }

    /// <summary>
    /// Provides means to cleanup after the call.
    /// If the call has already finished normally (request stream has been completed and call result has been received), doesn't do anything.
    /// Otherwise, requests cancellation of the call which should terminate all pending async operations associated with the call.
    /// As a result, all resources being used by the call should be released eventually.
    /// </summary>
    /// <remarks>
    /// Normally, there is no need for you to dispose the call unless you want to utilize the
    /// "Cancel" semantics of invoking <c>Dispose</c>.
    /// </remarks>
    public void Dispose()
    {
        callState.Dispose();
    }

    private string DebuggerToString() => CallDebuggerHelpers.DebuggerToString(callState);

    private sealed class AsyncClientStreamingCallDebugView
    {
        private readonly AsyncClientStreamingCall<TRequest, TResponse> _call;

        public AsyncClientStreamingCallDebugView(AsyncClientStreamingCall<TRequest, TResponse> call)
        {
            _call = call;
        }

        public CallDebuggerMethodDebugView? Method => _call.callState.State is IMethod method ? new CallDebuggerMethodDebugView(method) : null;
        public bool IsComplete => CallDebuggerHelpers.GetStatus(_call.callState) != null;
        public Status? Status => CallDebuggerHelpers.GetStatus(_call.callState);
        public Metadata? ResponseHeaders => _call.ResponseHeadersAsync.Status == TaskStatus.RanToCompletion ? _call.ResponseHeadersAsync.GetAwaiter().GetResult() : null;
        public Metadata? Trailers => CallDebuggerHelpers.GetTrailers(_call.callState);
        public IClientStreamWriter<TRequest> RequestStream => _call.RequestStream;
        public TResponse? Response => _call.ResponseAsync.Status == TaskStatus.RanToCompletion ? _call.ResponseAsync.Result : default;
    }
}
