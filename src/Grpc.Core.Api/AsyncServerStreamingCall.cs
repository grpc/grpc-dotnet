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
using System.Threading.Tasks;
using Grpc.Core.Internal;

namespace Grpc.Core;

/// <summary>
/// Return type for server streaming calls.
/// </summary>
/// <typeparam name="TResponse">Response message type for this call.</typeparam>
[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(AsyncServerStreamingCall<>.AsyncServerStreamingCallDebugView))]
public sealed class AsyncServerStreamingCall<TResponse> : IDisposable
{
    readonly IAsyncStreamReader<TResponse> responseStream;
    readonly AsyncCallState callState;

    /// <summary>
    /// Creates a new AsyncDuplexStreamingCall object with the specified properties.
    /// </summary>
    /// <param name="responseStream">Stream of response values.</param>
    /// <param name="responseHeadersAsync">Response headers of the asynchronous call.</param>
    /// <param name="getStatusFunc">Delegate returning the status of the call.</param>
    /// <param name="getTrailersFunc">Delegate returning the trailing metadata of the call.</param>
    /// <param name="disposeAction">Delegate to invoke when Dispose is called on the call object.</param>
    public AsyncServerStreamingCall(IAsyncStreamReader<TResponse> responseStream,
                                    Task<Metadata> responseHeadersAsync,
                                    Func<Status> getStatusFunc,
                                    Func<Metadata> getTrailersFunc,
                                    Action disposeAction)
    {
        this.responseStream = responseStream;
        this.callState = new AsyncCallState(responseHeadersAsync, getStatusFunc, getTrailersFunc, disposeAction);
    }

    /// <summary>
    /// Creates a new AsyncDuplexStreamingCall object with the specified properties.
    /// </summary>
    /// <param name="responseStream">Stream of response values.</param>
    /// <param name="responseHeadersAsync">Response headers of the asynchronous call.</param>
    /// <param name="getStatusFunc">Delegate returning the status of the call.</param>
    /// <param name="getTrailersFunc">Delegate returning the trailing metadata of the call.</param>
    /// <param name="disposeAction">Delegate to invoke when Dispose is called on the call object.</param>
    /// <param name="state">State object for use with the callback parameters.</param>
    public AsyncServerStreamingCall(IAsyncStreamReader<TResponse> responseStream,
                                    Func<object, Task<Metadata>> responseHeadersAsync,
                                    Func<object, Status> getStatusFunc,
                                    Func<object, Metadata> getTrailersFunc,
                                    Action<object> disposeAction,
                                    object state)
    {
        this.responseStream = responseStream;
        this.callState = new AsyncCallState(responseHeadersAsync, getStatusFunc, getTrailersFunc, disposeAction, state);
    }

    /// <summary>
    /// Async stream to read streaming responses.
    /// </summary>
    public IAsyncStreamReader<TResponse> ResponseStream
    {
        get
        {
            return responseStream;
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
    /// If the call has already finished normally (response stream has been fully read), doesn't do anything.
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

    private sealed class AsyncServerStreamingCallDebugView
    {
        private readonly AsyncServerStreamingCall<TResponse> _call;

        public AsyncServerStreamingCallDebugView(AsyncServerStreamingCall<TResponse> call)
        {
            _call = call;
        }

        public bool IsComplete => CallDebuggerHelpers.GetStatus(_call.callState) != null;
        public Status? Status => CallDebuggerHelpers.GetStatus(_call.callState);
        public Metadata? ResponseHeaders => _call.ResponseHeadersAsync.Status == TaskStatus.RanToCompletion ? _call.ResponseHeadersAsync.Result : null;
        public Metadata? Trailers => CallDebuggerHelpers.GetTrailers(_call.callState);
        public IAsyncStreamReader<TResponse> ResponseStream => _call.ResponseStream;
        public CallDebuggerMethodDebugView? Method => CallDebuggerHelpers.GetDebugValue<IMethod>(_call.callState, CallDebuggerHelpers.MethodKey) is { } method ? new CallDebuggerMethodDebugView(method) : null;
        public ChannelBase? Channel => CallDebuggerHelpers.GetDebugValue<ChannelBase>(_call.callState, CallDebuggerHelpers.ChannelKey);
        public object? Request => CallDebuggerHelpers.GetDebugValue<object>(_call.callState, CallDebuggerHelpers.RequestKey);
    }
}
