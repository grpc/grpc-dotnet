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
using Greet;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FunctionalTestsWebsite.Services
{
    public class GreeterService : Greeter.GreeterBase
    {
        private readonly ILogger _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GreeterService(ILoggerFactory loggerFactory, IHttpContextAccessor httpContextAccessor)
        {
            _logger = loggerFactory.CreateLogger<GreeterService>();
            _httpContextAccessor = httpContextAccessor;
        }

        //Server side handler of the SayHello RPC
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            _logger.LogInformation($"Sending hello to {request.Name}");
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }

        public override Task<HelloReply> SayHelloWithHttpContextAccessor(HelloRequest request, ServerCallContext context)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            context.ResponseTrailers.Add("Test-HttpContext-PathAndQueryString", httpContext.Request.Path + httpContext.Request.QueryString);

            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }

        public override Task<HelloReply> SayHelloWithHttpContextExtensionMethod(HelloRequest request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            context.ResponseTrailers.Add("Test-HttpContext-PathAndQueryString", httpContext.Request.Path + httpContext.Request.QueryString);

            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }

        public override Task<HelloReply> SayHelloReturnNull(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult<HelloReply>(null);
        }

        public override Task<HelloReply> SayHelloThrowExceptionWithTrailers(HelloRequest request, ServerCallContext context)
        {
            var trailers = new Metadata();
            trailers.Add(new Metadata.Entry("test-trailer", "A value!"));

            return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.Unknown, "User error"), trailers));
        }

        public override Task<HelloReply> SayHelloSendLargeReply(HelloRequest request, ServerCallContext context)
        {
            _logger.LogInformation($"Sending hello to {request.Name}");
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name + new string('!', 1000000) });
        }

        public override async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            // Explicitly send the response headers before any streamed content
            Metadata responseHeaders = new Metadata();
            responseHeaders.Add("test-response-header", "value");
            await context.WriteResponseHeadersAsync(responseHeaders);

            await SayHellosCore(request, responseStream);
        }

        public override async Task SayHellosSendHeadersFirst(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(null);

            await SayHellosCore(request, responseStream);
        }

        public override async Task SayHellosDeadline(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            var i = 0;
            while (DateTime.UtcNow < context.Deadline)
            {
                var message = $"How are you {request.Name}? {i}";
                await responseStream.WriteAsync(new HelloReply { Message = message });

                i++;

                await Task.Delay(110);
            }

            // Ensure deadline timer has run
            var tcs = new TaskCompletionSource<object>();
            context.CancellationToken.Register(() => tcs.SetResult(null));
            await tcs.Task;
        }

        public override async Task SayHellosDeadlineCancellationToken(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            var i = 0;
            while (!context.CancellationToken.IsCancellationRequested)
            {
                var message = $"How are you {request.Name}? {i}";
                await responseStream.WriteAsync(new HelloReply { Message = message });

                i++;

                await Task.Delay(110);
            }
        }

        public static async Task SayHellosCore(HelloRequest request, IServerStreamWriter<HelloReply> responseStream)
        {
            for (var i = 0; i < 3; i++)
            {
                // Gotta look busy
                await Task.Delay(100);

                var message = $"How are you {request.Name}? {i}";
                await responseStream.WriteAsync(new HelloReply { Message = message });
            }

            // Gotta look busy
            await Task.Delay(100);

            await responseStream.WriteAsync(new HelloReply { Message = $"Goodbye {request.Name}!" });
        }
    }
}
