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

using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Chat;
using Google.Protobuf;
using Grpc.AspNetCore.Performance.Internal;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grpc.AspNetCore.Performance
{
    public class UnaryServerCallHandlerBenchmark
    {
        private UnaryServerCallHandler<ChatMessage, ChatMessage, TestService> _callHandler;
        private ServiceProvider _requestServices;
        private DefaultHttpContext _httpContext;
        private HeaderDictionary _trailers;
        private MemoryStream _requestStream;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var marshaller = Marshallers.Create((arg) => MessageExtensions.ToByteArray(arg), bytes => new ChatMessage());
            var method = new Method<ChatMessage, ChatMessage>(MethodType.Unary, typeof(TestService).FullName, nameof(TestService.SayHello), marshaller, marshaller);
            _callHandler = new UnaryServerCallHandler<ChatMessage, ChatMessage, TestService>(method, new GrpcServiceOptions());

            _trailers = new HeaderDictionary();

            var messageData = new ChatMessage
            {
                Name = "Joe"
            }.ToByteArray();

            _requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(_requestStream, messageData);

            var services = new ServiceCollection();
            services.TryAddSingleton<IGrpcServiceActivator<TestService>>(new TestGrpcServiceActivator<TestService>(new TestService()));
            _requestServices = services.BuildServiceProvider();

            _httpContext = new DefaultHttpContext();
            _httpContext.RequestServices = _requestServices;
            _httpContext.Request.Body = _requestStream;

            _httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature
            {
                Trailers = _trailers
            });
        }

        [Benchmark]
        public Task HandleCallAsync()
        {
            _httpContext.Response.Headers.Clear();
            _requestStream.Seek(0, SeekOrigin.Begin);
            _trailers.Clear();

            return _callHandler.HandleCallAsync(_httpContext);
        }
    }
}
