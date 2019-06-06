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
using System.Threading;
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
                GrpcProtocolHelpers.SendHttpError(httpContext.Response, StatusCodes.Status415UnsupportedMediaType, StatusCode.Internal, error!);
                return Task.CompletedTask;
            }

            var serverCallContext = CreateServerCallContext(httpContext);

            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            try
            {
                serverCallContext.Initialize();

                var handleCallTask = HandleCallAsyncCore(httpContext, serverCallContext);
                if (serverCallContext.Timeout == TimeSpan.Zero)
                {
                    // Non-deadline request
                    serverCallContext.SetCancellationToken(httpContext.RequestAborted);

                    if (handleCallTask.IsCompletedSuccessfully)
                    {
                        return serverCallContext.EndCallAsync();
                    }
                    else
                    {
                        return AwaitHandleCall(serverCallContext, Method, handleCallTask);
                    }
                }
                else
                {
                    return HandleCallWithDeadline(httpContext, serverCallContext, handleCallTask);
                }
            }
            catch (Exception ex)
            {
                serverCallContext.ProcessHandlerError(ex, Method.Name);
                return Task.CompletedTask;
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
                    serverCallContext.ProcessHandlerError(ex, method.Name);
                }
            }
        }

        private async Task HandleCallWithDeadline(HttpContext httpContext, HttpContextServerCallContext serverCallContext, Task handleCallTask)
        {
            // CTS is used to...
            // 1. Cancel Task.Delay if the handler completes first
            // 2. CT is set on ServerCallContext to indicate
            var cts = new CancellationTokenSource();
            CancellationTokenRegistration registration = default;

            serverCallContext.SetCancellationToken(cts.Token);

            try
            {
                // Cancel the CTS if the request is aborted
                if (httpContext.RequestAborted.CanBeCanceled)
                {
                    registration = httpContext.RequestAborted.Register((c) =>
                    {
                        ((CancellationTokenSource)c).Cancel();
                    }, cts, false);
                }

                var completedTask = await Task.WhenAny(
                    handleCallTask,
                    Task.Delay(serverCallContext.Timeout, cts.Token));

                // Either the call handler is complete, and we want to cancel the Task.Delay
                // Or the deadline has been exceeded and we want to trigger ServerCallContext.CancellationToken
                cts.Cancel();

                if (completedTask != handleCallTask)
                {
                    serverCallContext.DeadlineExceeded();
                    httpContext.Response.ConsolidateTrailers(serverCallContext);

                    // Ensure any errors thrown by the dead call are handled
                    _ = ObserveDeadCall(serverCallContext, handleCallTask);
                }
                else
                {
                    await serverCallContext.EndCallAsync();
                }
            }
            finally
            {
                registration.Dispose();
            }
        }

        private async Task ObserveDeadCall(HttpContextServerCallContext serverCallContext, Task handleCallTask)
        {
            try
            {
                await handleCallTask;
            }
            catch
            {
                // TODO: Log errors from dead calls
            }
        }

        protected abstract Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext);

        protected HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext)
        {
            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContext);

            return serverCallContext;
        }

        /// <summary>
        /// This should only be called from client streaming calls.
        /// </summary>
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
