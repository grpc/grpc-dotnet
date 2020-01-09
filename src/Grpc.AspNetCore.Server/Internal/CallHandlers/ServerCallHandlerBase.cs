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
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal abstract class ServerCallHandlerBase<TService, TRequest, TResponse>
        where TService : class
        where TRequest : class
        where TResponse : class
    {
        private const string LoggerName = "Grpc.AspNetCore.Server.ServerCallHandler";

        protected ServerMethodInvokerBase<TService, TRequest, TResponse> MethodInvoker { get; }
        protected ILogger Logger { get; }

        protected ServerCallHandlerBase(
            ServerMethodInvokerBase<TService, TRequest, TResponse> methodInvoker,
            ILoggerFactory loggerFactory)
        {
            MethodInvoker = methodInvoker;
            Logger = loggerFactory.CreateLogger(LoggerName);
        }

        public Task HandleCallAsync(HttpContext httpContext)
        {
            if (GrpcProtocolHelpers.IsInvalidContentType(httpContext, out var error))
            {
                // This might be a CORS preflight request and CORS middleware hasn't been configured
                if (HttpMethods.IsOptions(httpContext.Request.Method))
                {
                    GrpcServerLog.UnhandledCorsPreflightRequest(Logger);

                    GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status405MethodNotAllowed, StatusCode.Internal, "Unhandled CORS preflight request received. CORS may not be configured correctly in the application.");
                    httpContext.Response.Headers[HeaderNames.Allow] = HttpMethods.Post;
                    return Task.CompletedTask;
                }
                else
                {
                    GrpcServerLog.UnsupportedRequestContentType(Logger, httpContext.Request.ContentType);

                    GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status415UnsupportedMediaType, StatusCode.Internal, error);
                    return Task.CompletedTask;
                }
            }
            if (httpContext.Request.Protocol != GrpcProtocolConstants.Http2Protocol &&
                httpContext.Request.Protocol != GrpcProtocolConstants.Http20Protocol)
            {
                GrpcServerLog.UnsupportedRequestProtocol(Logger, httpContext.Request.Protocol);

                var protocolError = $"Request protocol '{httpContext.Request.Protocol}' is not supported.";
                GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status426UpgradeRequired, StatusCode.Internal, protocolError);
                httpContext.Response.Headers[HeaderNames.Upgrade] = GrpcProtocolConstants.Http2Protocol;
                return Task.CompletedTask;
            }

            var serverCallContext = new HttpContextServerCallContext(httpContext, MethodInvoker.Options, typeof(TRequest), typeof(TResponse), Logger);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContext);

            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            try
            {
                serverCallContext.Initialize();

                var handleCallTask = HandleCallAsyncCore(httpContext, serverCallContext);

                if (handleCallTask.IsCompletedSuccessfully)
                {
                    return serverCallContext.EndCallAsync();
                }
                else
                {
                    return AwaitHandleCall(serverCallContext, MethodInvoker.Method, handleCallTask);
                }
            }
            catch (Exception ex)
            {
                return serverCallContext.ProcessHandlerErrorAsync(ex, MethodInvoker.Method.Name);
            }

            static async Task AwaitHandleCall(HttpContextServerCallContext serverCallContext, Method<TRequest, TResponse> method, Task handleCall)
            {
                try
                {
                    await handleCall;
                    await serverCallContext.EndCallAsync();
                }
                catch (Exception ex)
                {
                    await serverCallContext.ProcessHandlerErrorAsync(ex, method.Name);
                }
            }
        }

        protected abstract Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext);

        /// <summary>
        /// This should only be called from client streaming calls
        /// </summary>
        /// <param name="httpContext"></param>
        protected void DisableMinRequestBodyDataRateAndMaxRequestBodySize(HttpContext httpContext)
        {
            var minRequestBodyDataRateFeature = httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
            if (minRequestBodyDataRateFeature != null)
            {
                minRequestBodyDataRateFeature.MinDataRate = null;
            }

            var maxRequestBodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxRequestBodySizeFeature != null)
            {
                if (!maxRequestBodySizeFeature.IsReadOnly)
                {
                    maxRequestBodySizeFeature.MaxRequestBodySize = null;
                }
                else
                {
                    // IsReadOnly could be true if middleware has already started reading the request body
                    // In that case we can't disable the max request body size for the request stream
                    GrpcServerLog.UnableToDisableMaxRequestBodySize(Logger);
                }
            }
        }
    }
}
