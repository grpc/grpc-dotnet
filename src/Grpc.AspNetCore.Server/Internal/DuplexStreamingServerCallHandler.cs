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
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class DuplexStreamingServerCallHandler<TRequest, TResponse, TService> : ServerCallHandlerBase<TRequest, TResponse, TService>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        // We're using an open delegate (the first argument is the TService instance) to represent the call here since the instance is create per request.
        // This is the reason we're not using the delegates defined in Grpc.Core. This delegate maps to DuplexStreamingServerMethod<TRequest, TResponse>
        // with an instance parameter.
        private delegate Task DuplexStreamingServerMethod(TService service, IAsyncStreamReader<TRequest> input, IServerStreamWriter<TResponse> output, ServerCallContext serverCallContext);

        private readonly DuplexStreamingServerMethod _invoker;

        public DuplexStreamingServerCallHandler(Method<TRequest, TResponse> method, GrpcServiceOptions serviceOptions) : base(method, serviceOptions)
        {
            var handlerMethod = typeof(TService).GetMethod(Method.Name);

            _invoker = (DuplexStreamingServerMethod)Delegate.CreateDelegate(typeof(DuplexStreamingServerMethod), handlerMethod);
        }

        public override async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            var service = activator.Create();
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            var streamWriter = new HttpContextStreamWriter<TResponse>(serverCallContext, ServiceOptions, Method.ResponseMarshaller.Serializer);

            serverCallContext.Initialize();

            using (serverCallContext)
            {
                await _invoker(
                    service,
                    new HttpContextStreamReader<TRequest>(httpContext, ServiceOptions, Method.RequestMarshaller.Deserializer),
                    streamWriter,
                    serverCallContext);
            }

            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Flush any buffered content
            await httpContext.Response.BodyPipe.FlushAsync();
        }
    }
}
