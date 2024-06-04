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

using Grpc.Core;

namespace Grpc.Net.ClientFactory.Internal;

internal sealed class CallOptionsConfigurationInvoker : CallInvoker
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IList<Action<CallOptionsContext>> _callOptionsActions;
    private readonly CallInvoker _innerInvoker;

    public CallOptionsConfigurationInvoker(CallInvoker innerInvoker, IList<Action<CallOptionsContext>> callOptionsActions, IServiceProvider serviceProvider)
    {
        _innerInvoker = innerInvoker;
        _callOptionsActions = callOptionsActions;
        _serviceProvider = serviceProvider;
    }

    // Internal for testing.
    internal CallInvoker InnerInvoker => _innerInvoker;

    private CallOptions ResolveCallOptions(CallOptions callOptions)
    {
        var context = new CallOptionsContext(callOptions, _serviceProvider);

        for (var i = 0; i < _callOptionsActions.Count; i++)
        {
            _callOptionsActions[i](context);
        }

        return context.CallOptions;
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        return _innerInvoker.AsyncClientStreamingCall(method, host, ResolveCallOptions(options));
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        return _innerInvoker.AsyncDuplexStreamingCall(method, host, ResolveCallOptions(options));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        return _innerInvoker.AsyncServerStreamingCall(method, host, ResolveCallOptions(options), request);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        return _innerInvoker.AsyncUnaryCall(method, host, ResolveCallOptions(options), request);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        return _innerInvoker.BlockingUnaryCall(method, host, ResolveCallOptions(options), request);
    }
}
