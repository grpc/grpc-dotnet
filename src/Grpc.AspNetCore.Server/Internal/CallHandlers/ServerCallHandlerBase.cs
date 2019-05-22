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
using Grpc.AspNetCore.Server.Features;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal abstract class ServerCallHandlerBase<TService, TRequest, TResponse> : IServerCallHandler
    {
        protected Method<TRequest, TResponse> Method { get; }
        protected GrpcServiceOptions ServiceOptions { get; }
        protected ILogger Logger { get; }

        protected ServerCallHandlerBase(Method<TRequest, TResponse> method, GrpcServiceOptions serviceOptions, ILoggerFactory loggerFactory)
        {
            Method = method;
            ServiceOptions = serviceOptions;
            Logger = loggerFactory.CreateLogger(typeof(TService));
        }

        public Task HandleCallAsync(HttpContext httpContext)
        {
            if (GrpcProtocolHelpers.IsInvalidContentType(httpContext, out var error))
            {
                GrpcProtocolHelpers.SendHttpError(httpContext.Response, StatusCodes.Status415UnsupportedMediaType, StatusCode.Internal, error);
                return Task.CompletedTask;
            }

            return HandleCallAsyncCore(httpContext);
        }

        protected abstract Task HandleCallAsyncCore(HttpContext httpContext);

        protected HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext)
        {
            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContext);

            return serverCallContext;
        }

        /// <summary>
        /// This should only be called from client streaming calls
        /// </summary>
        /// <param name="httpContext"></param>
        protected void DisableMinRequestBodyDataRate(HttpContext httpContext)
        {
            var minRequestBodyDataRateFeature = httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
            if (minRequestBodyDataRateFeature != null)
            {
                minRequestBodyDataRateFeature.MinDataRate = null;
            }
        }
    }
}
