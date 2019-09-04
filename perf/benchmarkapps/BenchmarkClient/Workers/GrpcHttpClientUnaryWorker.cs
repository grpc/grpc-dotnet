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
using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

namespace BenchmarkClient.Workers
{
    public class GrpcHttpClientUnaryWorker : IWorker
    {
        private readonly bool _useClientCertificate;
        private readonly DateTime? _deadline;
        private GreetService.GreetServiceClient? _client;

        public GrpcHttpClientUnaryWorker(int id, string target, bool useClientCertificate, DateTime? deadline = null)
        {
            Id = id;
            Target = target;
            _useClientCertificate = useClientCertificate;
            _deadline = deadline;
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            var options = new CallOptions(deadline: _deadline);
            var call = _client!.SayHelloAsync(new HelloRequest { Name = "World" }, options);
            await call.ResponseAsync;
        }

        public Task ConnectAsync()
        {
            var url = _useClientCertificate ? "https://" : "http://";
            url += Target;

            var channel = GrpcChannel.ForAddress(new Uri(url, UriKind.RelativeOrAbsolute));
            _client = new GreetService.GreetServiceClient(channel);

            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }
    }
}
