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

using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model
{
    // Open delegate (the first argument is the TService instance) versions of the service call types.
    // Needed because methods are executed with a new service instance each request.

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <param name="service"></param>
    /// <param name="request"></param>
    /// <param name="serverCallContext"></param>
    /// <returns></returns>
    public delegate Task<TResponse> UnaryServerMethod<TService, TRequest, TResponse>(TService service, TRequest request, ServerCallContext serverCallContext);

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <param name="service"></param>
    /// <param name="stream"></param>
    /// <param name="serverCallContext"></param>
    /// <returns></returns>
    public delegate Task<TResponse> ClientStreamingServerMethod<TService, TRequest, TResponse>(TService service, IAsyncStreamReader<TRequest> stream, ServerCallContext serverCallContext);

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <param name="service"></param>
    /// <param name="request"></param>
    /// <param name="stream"></param>
    /// <param name="serverCallContext"></param>
    /// <returns></returns>
    public delegate Task ServerStreamingServerMethod<TService, TRequest, TResponse>(TService service, TRequest request, IServerStreamWriter<TResponse> stream, ServerCallContext serverCallContext);

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <param name="service"></param>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <param name="serverCallContext"></param>
    /// <returns></returns>
    public delegate Task DuplexStreamingServerMethod<TService, TRequest, TResponse>(TService service, IAsyncStreamReader<TRequest> input, IServerStreamWriter<TResponse> output, ServerCallContext serverCallContext);
}
