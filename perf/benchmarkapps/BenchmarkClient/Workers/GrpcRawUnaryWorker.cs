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
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;

namespace BenchmarkClient.Workers
{
    public class GrpcRawUnaryWorker : IWorker
    {
        private HttpClient? _client;

        public GrpcRawUnaryWorker(int id, string target)
        {
            Id = id;
            Target = target;
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            var message = new HelloRequest
            {
                Name = "World"
            };

            var messageSize = message.CalculateSize();
            var messageBytes = new byte[messageSize];
            message.WriteTo(new CodedOutputStream(messageBytes));

            var data = new byte[messageSize + 5];
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);
            messageBytes.CopyTo(data.AsSpan(5));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://" + Target + "/Greet.Greeter/SayHello");
            request.Version = new Version(2, 0);
            request.Content = new StreamContent(new MemoryStream(data));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

            var response = await _client!.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await response.Content.ReadAsByteArrayAsync();

            var grpcStatus = response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault();
            if (grpcStatus != "0")
            {
                throw new InvalidOperationException($"Unexpected grpc-status: {grpcStatus}");
            }
        }

        public Task ConnectAsync()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;

            _client = new HttpClient(handler);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _client?.Dispose();
            return Task.CompletedTask;
        }
    }
}
