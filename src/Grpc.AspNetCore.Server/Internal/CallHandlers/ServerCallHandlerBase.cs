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
        protected Method<TRequest, TResponse> Method { get; }
        protected GrpcServiceOptions ServiceOptions { get; }
        protected IGrpcServiceActivator<TService> ServiceActivator { get; }
        protected IServiceProvider ServiceProvider { get; }
        protected ILogger Logger { get; }

        protected ServerCallHandlerBase(
            Method<TRequest, TResponse> method,
            GrpcServiceOptions serviceOptions,
            ILoggerFactory loggerFactory,
            IGrpcServiceActivator<TService> serviceActivator,
            IServiceProvider serviceProvider)
        {
            Method = method;
            ServiceOptions = serviceOptions;
            ServiceActivator = serviceActivator;
            ServiceProvider = serviceProvider;
            Logger = loggerFactory.CreateLogger(typeof(TService));
        }

        public Task HandleCallAsync(HttpContext httpContext)
        {
            if (GrpcProtocolHelpers.IsInvalidContentType(httpContext, out var error))
            {
                GrpcProtocolHelpers.SendHttpError(httpContext.Response, StatusCodes.Status415UnsupportedMediaType, StatusCode.Internal, error);
                return Task.CompletedTask;
            }
            if (httpContext.Request.Protocol != GrpcProtocolConstants.Http2Protocol)
            {
                var protocolError = $"Request protocol '{httpContext.Request.Protocol}' is not supported.";
                GrpcProtocolHelpers.SendHttpError(httpContext.Response, StatusCodes.Status426UpgradeRequired, StatusCode.Internal, protocolError);
                httpContext.Response.Headers[HeaderNames.Upgrade] = GrpcProtocolConstants.Http2Protocol;
                return Task.CompletedTask;
            }

            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);
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
                    return AwaitHandleCall(serverCallContext, Method, handleCallTask);
                }
            }
            catch (Exception ex)
            {
                return serverCallContext.ProcessHandlerErrorAsync(ex, Method.Name);
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
                    Log.UnableToDisableMaxRequestBodySize(Logger);
                }
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception?> _unableToDisableMaxRequestBodySize =
                LoggerMessage.Define(LogLevel.Debug, new EventId(1, "UnableToDisableMaxRequestBodySizeLimit"), "Unable to disable the max request body size limit.");

            public static void UnableToDisableMaxRequestBodySize(ILogger logger)
            {
                _unableToDisableMaxRequestBodySize(logger, null);
            }
        }
    }
}
