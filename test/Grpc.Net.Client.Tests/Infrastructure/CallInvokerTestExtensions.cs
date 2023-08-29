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

using Greet;
using Grpc.Core;
using Grpc.Tests.Shared;

namespace Grpc.Net.Client.Tests.Infrastructure;

internal static class CallInvokerTestExtensions
{
    public static AsyncClientStreamingCall<HelloRequest, HelloReply> AsyncClientStreamingCall(this CallInvoker callInvoker, CallOptions? options = null)
    {
        return callInvoker.AsyncClientStreamingCall(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, options ?? new CallOptions());
    }

    public static AsyncDuplexStreamingCall<HelloRequest, HelloReply> AsyncDuplexStreamingCall(this CallInvoker callInvoker, CallOptions? options = null)
    {
        return callInvoker.AsyncDuplexStreamingCall(ClientTestHelpers.GetServiceMethod(MethodType.DuplexStreaming), string.Empty, options ?? new CallOptions());
    }

    public static AsyncServerStreamingCall<HelloReply> AsyncServerStreamingCall(this CallInvoker callInvoker, HelloRequest request, CallOptions? options = null)
    {
        return callInvoker.AsyncServerStreamingCall(ClientTestHelpers.GetServiceMethod(MethodType.ServerStreaming), string.Empty, options ?? new CallOptions(), request);
    }

    public static AsyncUnaryCall<HelloReply> AsyncUnaryCall(this CallInvoker callInvoker, HelloRequest request, CallOptions? options = null)
    {
        return callInvoker.AsyncUnaryCall(ClientTestHelpers.GetServiceMethod(MethodType.Unary), string.Empty, options ?? new CallOptions(), request);
    }
}
