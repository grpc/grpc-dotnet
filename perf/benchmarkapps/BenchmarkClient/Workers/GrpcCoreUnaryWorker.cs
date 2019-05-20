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
using System.Reflection;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;

namespace BenchmarkClient.Workers
{
    public class GrpcCoreUnaryWorker : IWorker
    {
        private Channel? _channel;
        private Greeter.GreeterClient? _client;
        private bool _useClientCertificate;

        public GrpcCoreUnaryWorker(int id, string target, bool useClientCertificate)
        {
            Id = id;
            Target = target;
            _useClientCertificate = useClientCertificate;
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            var call = _client!.SayHelloAsync(new HelloRequest { Name = "World" });
            await call.ResponseAsync;
        }

        public Task ConnectAsync()
        {
            var credentials = _useClientCertificate
                ? GetCertificateCredentials()
                : ChannelCredentials.Insecure;

            _channel = new Channel(Target, credentials);
            _client = new Greeter.GreeterClient(_channel);

            return _channel.ConnectAsync();
        }

        public Task DisconnectAsync()
        {
            return _channel?.ShutdownAsync() ?? Task.CompletedTask;
        }

        private static ChannelCredentials GetCertificateCredentials()
        {
            var currentPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            return new SslCredentials(
              File.ReadAllText(Path.Combine(currentPath, "Certs", "ca.crt")),
              new KeyCertificatePair(
                  File.ReadAllText(Path.Combine(currentPath, "Certs", "client.crt")),
                  File.ReadAllText(Path.Combine(currentPath, "Certs", "client.key"))));
        }
    }
}
