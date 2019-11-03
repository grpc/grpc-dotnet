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
    /// Server-side handler for a unary call.
    /// </summary>
    /// <typeparam name="TService">Service type for this method.</typeparam>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="service">The service instance.</param>
    /// <param name="request">The request message.</param>
    /// <param name="serverCallContext">The <see cref="ServerCallContext"/> for the call.</param>
    /// <returns>
    /// A task that represents the completion of the call. The <see cref="Task{TResult}.Result"/>
    /// property returns a <typeparamref name="TResponse"/> for the call response.
    /// </returns>
    public delegate Task<TResponse> UnaryServerMethod<TService, TRequest, TResponse>(TService service, TRequest request, ServerCallContext serverCallContext);

    /// <summary>
    /// Server-side handler for a client streaming call.
    /// </summary>
    /// <typeparam name="TService">Service type for this method.</typeparam>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="service">The service instance.</param>
    /// <param name="stream">A <see cref="IAsyncStreamReader{TRequest}"/> that is used to read a stream of request messages.</param>
    /// <param name="serverCallContext">The <see cref="ServerCallContext"/> for the call.</param>
    /// <returns>
    /// A task that represents the completion of the call. The <see cref="Task{TResult}.Result"/>
    /// property returns a <typeparamref name="TResponse"/> for the call response.
    /// </returns>
    public delegate Task<TResponse> ClientStreamingServerMethod<TService, TRequest, TResponse>(TService service, IAsyncStreamReader<TRequest> stream, ServerCallContext serverCallContext);

    /// <summary>
    /// Server-side handler for a server streaming call.
    /// </summary>
    /// <typeparam name="TService">Service type for this method.</typeparam>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="service">The service instance.</param>
    /// <param name="request">The request message.</param>
    /// <param name="stream">A <see cref="IServerStreamWriter{TResponse}"/> that is used to write a stream of response messages.</param>
    /// <param name="serverCallContext">The <see cref="ServerCallContext"/> for the call.</param>
    /// <returns>A task that represents the completion of the call.</returns>
    public delegate Task ServerStreamingServerMethod<TService, TRequest, TResponse>(TService service, TRequest request, IServerStreamWriter<TResponse> stream, ServerCallContext serverCallContext);

    /// <summary>
    /// Server-side handler for a duplex streaming call.
    /// </summary>
    /// <typeparam name="TService">Service type for this method.</typeparam>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="service">The service instance.</param>
    /// <param name="input">A <see cref="IAsyncStreamReader{TRequest}"/> that is used to read a stream of request messages.</param>
    /// <param name="output">A <see cref="IServerStreamWriter{TResponse}"/> that is used to write a stream of response messages.</param>
    /// <param name="serverCallContext">The <see cref="ServerCallContext"/> for the call.</param>
    /// <returns>A task that represents the completion of the call.</returns>
    public delegate Task DuplexStreamingServerMethod<TService, TRequest, TResponse>(TService service, IAsyncStreamReader<TRequest> input, IServerStreamWriter<TResponse> output, ServerCallContext serverCallContext);
}
