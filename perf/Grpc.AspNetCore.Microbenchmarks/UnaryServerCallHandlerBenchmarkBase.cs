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

using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Chat;
using Google.Protobuf;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.AspNetCore.Microbenchmarks
{
    public class UnaryServerCallHandlerBenchmarkBase
    {
        private UnaryServerCallHandler<TestService, ChatMessage, ChatMessage>? _callHandler;
        private ServiceProvider? _requestServices;
        private DefaultHttpContext? _httpContext;
        private HeaderDictionary? _trailers;
        private byte[]? _requestMessage;
        private TestPipeReader? _requestPipe;

        internal GrpcServiceOptions ServiceOptions { get; } = new GrpcServiceOptions();

        [GlobalSetup]
        public void GlobalSetup()
        {
            var marshaller = Marshallers.Create((arg) => MessageExtensions.ToByteArray(arg), bytes => new ChatMessage());
            var method = new Method<ChatMessage, ChatMessage>(MethodType.Unary, typeof(TestService).FullName, nameof(TestService.SayHello), marshaller, marshaller);
            var result = Task.FromResult(new ChatMessage());
            _callHandler = new UnaryServerCallHandler<TestService, ChatMessage, ChatMessage>(
                method,
                (service, request, context) => result,
                ServiceOptions,
                NullLoggerFactory.Instance);

            _trailers = new HeaderDictionary();

            var message = new ChatMessage
            {
                Name = "Joe"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, message);
            _requestMessage = ms.ToArray();

            _requestPipe = new TestPipeReader();

            var services = new ServiceCollection();
            services.TryAddSingleton<IGrpcServiceActivator<TestService>>(new TestGrpcServiceActivator<TestService>(new TestService()));
            services.TryAddSingleton<IGrpcInterceptorActivator<UnaryAwaitInterceptor>>(new TestGrpcInterceptorActivator<UnaryAwaitInterceptor>(new UnaryAwaitInterceptor()));
            _requestServices = services.BuildServiceProvider();

            _httpContext = new DefaultHttpContext();
            _httpContext.RequestServices = _requestServices;
            _httpContext.Request.BodyReader = _requestPipe;
            _httpContext.Request.ContentType = GrpcProtocolConstants.GrpcContentType;
            _httpContext.Response.BodyWriter = new TestPipeWriter();

            _httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature
            {
                Trailers = _trailers
            });
        }

        protected Task InvokeUnaryRequestAsync()
        {
            _httpContext!.Response.Headers.Clear();
            _trailers!.Clear();
            _requestPipe!.ReadResults.Add(new ValueTask<ReadResult>(new ReadResult(new ReadOnlySequence<byte>(_requestMessage), false, true)));

            return _callHandler!.HandleCallAsync(_httpContext);
        }
    }
}
