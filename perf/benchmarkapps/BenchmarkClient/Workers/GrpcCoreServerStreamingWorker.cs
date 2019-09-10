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
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

namespace BenchmarkClient.Workers
{
    public class GrpcCoreServerStreamingWorker : IWorker
    {
        private readonly bool _useClientCertificate;
        private readonly DateTime? _deadline;
        private readonly CancellationTokenSource _cts;
        private Channel? _channel;
        private GreetService.GreetServiceClient? _client;
        private AsyncServerStreamingCall<HelloReply>? _call;

        public GrpcCoreServerStreamingWorker(int id, string target, bool useClientCertificate, DateTime? deadline = null)
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

        public async Task ConnectAsync()
        {
            var credentials = _useClientCertificate
                ? GetCertificateCredentials()
                : ChannelCredentials.Insecure;

            _channel = new Channel(Target, credentials);
            _client = new GreetService.GreetServiceClient(_channel);

            await _channel.ConnectAsync();

            var options = new CallOptions(deadline: _deadline, cancellationToken: _cts.Token);
            _call = _client.SayHellos(new HelloRequest { Name = "World" }, options);
        }

        public async Task DisconnectAsync()
        {
            _cts.Cancel();
            _call?.Dispose();
            if (_channel != null)
            {
                await _channel.ShutdownAsync();
            }
        }

        private static ChannelCredentials GetCertificateCredentials()
        {
            var currentPath = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location)!;

            return new SslCredentials(
              File.ReadAllText(Path.Combine(currentPath, "Certs", "ca.crt")),
              new KeyCertificatePair(
                  File.ReadAllText(Path.Combine(currentPath, "Certs", "client.crt")),
                  File.ReadAllText(Path.Combine(currentPath, "Certs", "client.key"))));
        }
    }
}
