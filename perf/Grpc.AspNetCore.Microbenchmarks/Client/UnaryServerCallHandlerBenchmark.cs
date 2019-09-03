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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Greet;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.Net.Client;
using Grpc.Tests.Shared;

namespace Grpc.AspNetCore.Microbenchmarks.Client
{
    public class UnaryClientBenchmark
    {
        private Greeter.GreeterClient? _client;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new HelloReply());
            var requestMessage = ms.ToArray();

            var handler = TestHttpMessageHandler.Create(r =>
            {
                var content = new ByteArrayContent(requestMessage);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
            });

            var httpClient = new HttpClient(handler);

            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            _client = new Greeter.GreeterClient(channel);
        }

        [Benchmark]
        public Task HandleCallAsync()
        {
            return _client!.SayHelloAsync(new HelloRequest()).ResponseAsync;
        }
    }
}
