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
    internal class ClientStreamingServerCallHandler<TRequest, TResponse, TService> : IServerCallHandler
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly Method<TRequest, TResponse> _method;

        public ClientStreamingServerCallHandler(Method<TRequest, TResponse> method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TService).GetMethod(_method.Name);

            // Invoke procedure
            var response = await (Task<TResponse>)handlerMethod.Invoke(
                service,
                new object[]
                {
                    new HttpContextStreamReader<TRequest>(httpContext, _method.RequestMarshaller.Deserializer),
                    null
                });

            // TODO: make sure the response is not null
            var responsePayload = _method.ResponseMarshaller.Serializer(response);

            await StreamUtils.WriteMessageAsync(httpContext.Response.Body, responsePayload, 0, responsePayload.Length);

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }
}
