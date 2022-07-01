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
using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using Chat;
using Google.Protobuf;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Shared.Server;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Grpc.AspNetCore.Microbenchmarks.Server
{
    public class UnaryServerCallHandlerBenchmarkBase
    {
        private UnaryServerCallHandler<TestService, ChatMessage, ChatMessage>? _callHandler;
        private ServiceProvider? _requestServices;
        private DefaultHttpContext? _httpContext;
        private HeaderDictionary? _trailers;
        private IHeaderDictionary? _headers;
        private byte[]? _requestMessage;
        private TestPipeReader? _requestPipe;

        protected InterceptorCollection? Interceptors { get; set; }
        protected List<ICompressionProvider>? CompressionProviders { get; set; }
        protected string? ResponseCompressionAlgorithm { get; set; }
        protected Grpc.AspNetCore.Server.Model.UnaryServerMethod<TestService, ChatMessage, ChatMessage>? Method { get; set; }
        protected string ExpectedStatus { get; set; } = "0";

        [GlobalSetup]
        public void GlobalSetup()
        {
            var message = new ChatMessage
            {
                Name =
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed at ligula nec orci placerat mollis. " +
                    "Interdum et malesuada fames ac ante ipsum primis in faucibus. Ut aliquet non nunc id lobortis. " +
                    "In tincidunt ac sapien sit amet consequat. Interdum et malesuada fames ac ante ipsum primis in faucibus. " +
                    "Duis vel tristique ipsum, eget hendrerit justo. Donec accumsan, purus quis cursus auctor, sapien nisi " +
                    "lacinia ligula, ut vehicula lorem augue vel est. Vestibulum finibus ornare vulputate."
            };

            var services = new ServiceCollection();
            services.TryAddSingleton<IGrpcInterceptorActivator<UnaryAwaitInterceptor>>(new TestGrpcInterceptorActivator<UnaryAwaitInterceptor>(new UnaryAwaitInterceptor()));
            var serviceProvider = services.BuildServiceProvider();

            var marshaller = CreateMarshaller();

            var method = new Method<ChatMessage, ChatMessage>(MethodType.Unary, typeof(TestService).FullName!, nameof(TestService.SayHello), marshaller, marshaller);
            var result = Task.FromResult(message);
            _callHandler = new UnaryServerCallHandler<TestService, ChatMessage, ChatMessage>(
                new UnaryServerMethodInvoker<TestService, ChatMessage, ChatMessage>(
                    Method ?? ((service, request, context) => result),
                    method,
                    HttpContextServerCallContextHelper.CreateMethodOptions(
                        compressionProviders: CompressionProviders,
                        responseCompressionAlgorithm: ResponseCompressionAlgorithm,
                        interceptors: Interceptors),
                    new TestGrpcServiceActivator<TestService>(new TestService())),
                NullLoggerFactory.Instance);

            _trailers = new HeaderDictionary();

            _requestMessage = GetMessageData(message);

            _requestPipe = new TestPipeReader();

            _requestServices = serviceProvider;

            _httpContext = new DefaultHttpContext();
            _httpContext.RequestServices = _requestServices;
            _httpContext.Request.ContentType = GrpcProtocolConstants.GrpcContentType;
            _httpContext.Request.Protocol = GrpcProtocolConstants.Http2Protocol;

            _httpContext.Features.Set<IRequestBodyPipeFeature>(new TestRequestBodyPipeFeature(_requestPipe));
            _httpContext.Features.Set<IHttpResponseBodyFeature>(new TestResponseBodyFeature(new TestPipeWriter()));
            _httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature
            {
                Trailers = _trailers
            });
            _headers = _httpContext.Response.Headers;
            SetupHttpContext(_httpContext);
        }

        protected virtual Marshaller<ChatMessage> CreateMarshaller()
        {
            var marshaller = Marshallers.Create((arg) => MessageExtensions.ToByteArray(arg), bytes => new ChatMessage());
            return marshaller;
        }

        protected virtual void SetupHttpContext(HttpContext httpContext)
        {
        }

        protected virtual byte[] GetMessageData(ChatMessage message)
        {
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, message);
            return ms.ToArray();
        }

        protected async Task InvokeUnaryRequestAsync()
        {
            _headers!.Clear();
            _trailers!.Clear();
            _requestPipe!.ReadResults.Add(new ValueTask<ReadResult>(new ReadResult(new ReadOnlySequence<byte>(_requestMessage!), false, true)));

            await _callHandler!.HandleCallAsync(_httpContext!);

            StringValues value;
            if (_trailers.TryGetValue("grpc-status", out value) || _headers.TryGetValue("grpc-status", out value))
            {
                if (!value.Equals(ExpectedStatus))
                {
                    throw new InvalidOperationException("Unexpected grpc-status: " + Enum.Parse<StatusCode>(value.ToString()));
                }
            }
            else
            {
                throw new InvalidOperationException("No grpc-status returned.");
            }
        }
    }
}
