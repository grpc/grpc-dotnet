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

namespace Grpc.AspNetCore.Server.Internal
{
    /// <summary>
    /// An interface for creating delegates used to invoke service methods on .NET types.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    internal interface IGrpcMethodInvokerFactory<TService>
    {
        UnaryServerMethod<TService, TRequest, TResponse> CreateUnaryInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method);
        ClientStreamingServerMethod<TService, TRequest, TResponse> CreateClientStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method);
        ServerStreamingServerMethod<TService, TRequest, TResponse> CreateServerStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method);
        DuplexStreamingServerMethod<TService, TRequest, TResponse> CreateDuplexStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method);
    }
}
