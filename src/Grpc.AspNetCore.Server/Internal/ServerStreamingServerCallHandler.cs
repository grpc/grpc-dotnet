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
    internal class ServerStreamingServerCallHandler<TRequest, TResponse, TService> : IServerCallHandler
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly Method<TRequest, TResponse> _method;

        public ServerStreamingServerCallHandler(Method<TRequest, TResponse> method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            var requestPayload = await StreamUtils.ReadMessageAsync(httpContext.Request.Body);
            // TODO: make sure the payload is not null
            var request = _method.RequestMarshaller.Deserializer(requestPayload);

            // TODO: make sure there are no more request messages.

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TService).GetMethod(_method.Name);

            // Invoke procedure
            await (Task)handlerMethod.Invoke(
                service,
                new object[]
                {
                    request,
                    new HttpContextStreamWriter<TResponse>(httpContext, _method.ResponseMarshaller.Serializer),
                    null
                });

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }
}
