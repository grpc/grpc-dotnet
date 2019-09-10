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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

namespace BenchmarkClient.Workers
{
    public class GrpcHttpClientServerStreamingWorker : IWorker
    {
        private readonly bool _useClientCertificate;
        private readonly DateTime? _deadline;
        private readonly CancellationTokenSource _cts;
        private GreetService.GreetServiceClient? _client;
        private AsyncServerStreamingCall<HelloReply>? _call;

        public GrpcHttpClientServerStreamingWorker(int id, string target, bool useClientCertificate, DateTime? deadline = null)
        {
            Id = id;
            Target = target;
            _useClientCertificate = useClientCertificate;
            _deadline = deadline;
            _cts = new CancellationTokenSource();
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            try
            {
                if (!await _call!.ResponseStream.MoveNext())
                {
                    throw new Exception("Unexpected end of stream.");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && _cts.IsCancellationRequested)
            {
                // Expected exception from canceling call
            }
        }

        public Task ConnectAsync()
        {
            var url = _useClientCertificate ? "https://" : "http://";
            url += Target;

            var channel = GrpcChannel.ForAddress(new Uri(url, UriKind.RelativeOrAbsolute));
            _client = new GreetService.GreetServiceClient(channel);

            var options = new CallOptions(deadline: _deadline, cancellationToken: _cts.Token);
            _call = _client.SayHellos(new HelloRequest { Name = "World" }, options);

            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _cts.Cancel();
            _call?.Dispose();

            return Task.CompletedTask;
        }
    }
}
