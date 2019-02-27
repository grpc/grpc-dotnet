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
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class UnaryServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {

        private readonly UnaryServerMethod<TService, TRequest, TResponse> _invoker;

        public UnaryServerCallHandler(Method<TRequest, TResponse> method, UnaryServerMethod<TService, TRequest, TResponse> invoker, GrpcServiceOptions serviceOptions, ILoggerFactory loggerFactory) : base(method, serviceOptions, loggerFactory)
        {
            _invoker = invoker;
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext)
        {
            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);

            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            TService service = null;

            TResponse response = null;
            try
            {
                serverCallContext.Initialize();

                var requestPayload = await httpContext.Request.BodyPipe.ReadSingleMessageAsync(serverCallContext);

                var request = Method.RequestMarshaller.Deserializer(requestPayload);

                service = activator.Create();

                response = await _invoker(
                    service,
                    request,
                    serverCallContext);

                if (response == null)
                {
                    // This is consistent with Grpc.Core when a null value is returned
                    throw new RpcException(new Status(StatusCode.Cancelled, "No message returned from method."));
                }

                var responseBodyPipe = httpContext.Response.BodyPipe;
                await responseBodyPipe.WriteMessageAsync(response, serverCallContext, Method.ResponseMarshaller.Serializer);
            }
            catch (Exception ex)
            {
                serverCallContext.ProcessHandlerError(ex, Method.Name);
            }
            finally
            {
                serverCallContext.Dispose();
                if (service != null)
                {
                    activator.Release(service);
                }
            }

            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Flush any buffered content
            await httpContext.Response.BodyPipe.FlushAsync();
        }
    }
}
