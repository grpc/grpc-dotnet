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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Greet;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;

namespace Grpc.AspNetCore.Microbenchmarks.Client
{
    public class UnaryClientBenchmarkBase
    {
        protected List<ICompressionProvider>? CompressionProviders { get; set; }
        protected string? ResponseCompressionAlgorithm { get; set; }

        private Greeter.GreeterClient? _client;
        private string? _content;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _content =
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed at ligula nec orci placerat mollis. " +
                "Interdum et malesuada fames ac ante ipsum primis in faucibus. Ut aliquet non nunc id lobortis. " +
                "In tincidunt ac sapien sit amet consequat. Interdum et malesuada fames ac ante ipsum primis in faucibus. " +
                "Duis vel tristique ipsum, eget hendrerit justo. Donec accumsan, purus quis cursus auctor, sapien nisi " +
                "lacinia ligula, ut vehicula lorem augue vel est. Vestibulum finibus ornare vulputate.";

            var requestMessage = GetMessageData(new HelloReply { Message = _content });

            var handler = TestHttpMessageHandler.Create(async r =>
            {
                await r.Content!.CopyToAsync(Stream.Null);

                var content = new ByteArrayContent(requestMessage);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, content, grpcEncoding: ResponseCompressionAlgorithm);
            });

            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler,
                CompressionProviders = CompressionProviders
            });

            _client = new Greeter.GreeterClient(channel);
        }

        protected virtual byte[] GetMessageData(HelloReply message)
        {
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, message);
            return ms.ToArray();
        }

        protected async Task InvokeSayHelloAsync(CallOptions options)
        {
            var response = await _client!.SayHelloAsync(new HelloRequest { Name = _content }, options).ResponseAsync;
            
            if (response.Message != _content)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        }
    }
}
