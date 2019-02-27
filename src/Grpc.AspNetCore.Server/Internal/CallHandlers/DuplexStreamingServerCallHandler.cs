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
    internal class DuplexStreamingServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly DuplexStreamingServerMethod<TService, TRequest, TResponse> _invoker;

        public DuplexStreamingServerCallHandler(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker, GrpcServiceOptions serviceOptions, ILoggerFactory loggerFactory) : base(method, serviceOptions, loggerFactory)
        {
            _invoker = invoker;
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext)
        {
            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);

            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            TService service = null;

            try
            {
                serverCallContext.Initialize();

                service = activator.Create();

                await _invoker(
                    service,
                    new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.Deserializer),
                    new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.Serializer),
                    serverCallContext);
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
