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
using Grpc.Net.Client;

namespace Grpc.Net.ClientFactory.Internal
{
    // This a marker used to check if the underlying channel should be disposed. gRPC clients
    // share a reference to an instance of this class, and when it goes out of scope the channel
    // is eligible to be disposed.
    internal sealed class LifetimeTrackingCallInvoker : CallInvoker
    {
        public CallInvoker InnerInvoker { get; }
        public GrpcChannel Channel { get; }

        public LifetimeTrackingCallInvoker(CallInvoker innerInvoker, GrpcChannel channel)
        {
            InnerInvoker = innerInvoker;
            Channel = channel;
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            return InnerInvoker.AsyncClientStreamingCall(method, host, options);
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            return InnerInvoker.AsyncDuplexStreamingCall(method, host, options);
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return InnerInvoker.AsyncServerStreamingCall(method, host, options, request);
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return InnerInvoker.AsyncUnaryCall(method, host, options, request);
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return InnerInvoker.BlockingUnaryCall(method, host, options, request);
        }
    }
}
