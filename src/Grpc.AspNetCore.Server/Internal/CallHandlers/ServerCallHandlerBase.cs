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
            if (!GrpcProtocolHelpers.IsValidContentType(httpContext, out var error))
            {
                return GrpcProtocolHelpers.SendHttpError(httpContext.Response, StatusCode.Internal, error);
            }

            return HandleCallAsyncCore(httpContext);
        }

        protected abstract Task HandleCallAsyncCore(HttpContext httpContext);
    }
}
