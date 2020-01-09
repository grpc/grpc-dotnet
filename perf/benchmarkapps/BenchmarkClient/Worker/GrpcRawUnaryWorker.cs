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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Net.Client.Web;
using Grpc.Testing;

namespace BenchmarkClient.Worker
{
    public class GrpcRawUnaryWorker : IWorker
    {
        private readonly bool _useClientCertificate;
        private readonly GrpcWebMode? _useGrpcWeb;
        private readonly DateTime? _deadline;
        private HttpClient? _client;

        public GrpcRawUnaryWorker(int id, string target, bool useClientCertificate, GrpcWebMode? useGrpcWeb, DateTime? deadline = null)
        {
            Id = id;
            Target = target;
            _useClientCertificate = useClientCertificate;
            _useGrpcWeb = useGrpcWeb;
            _deadline = deadline;
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            var message = new SimpleRequest
            {
                ResponseSize = 10
            };

            var messageSize = message.CalculateSize();
            var messageBytes = new byte[messageSize];
            message.WriteTo(new CodedOutputStream(messageBytes));

            var data = new byte[messageSize + 5];
            data[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), (uint)messageSize);
            messageBytes.CopyTo(data.AsSpan(5));

            var url = _useClientCertificate ? "https://" : "http://";
            url += Target;

            var request = new HttpRequestMessage(HttpMethod.Post, url + "/grpc.testing.BenchmarkService/UnaryCall");
            request.Version = new Version(2, 0);
            request.Content = new StreamContent(new MemoryStream(data));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            request.Headers.TE.Add(new TransferCodingWithQualityHeaderValue("trailers"));
            if (_deadline != null)
            {
                request.Headers.Add("grpc-timeout", "1S");
            }

            var response = await _client!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            while (await stream.ReadAsync(data) > 0)
            {

            }

            var grpcStatus = response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault();
            if (grpcStatus != "0")
            {
                throw new InvalidOperationException($"Unexpected grpc-status: {grpcStatus}");
            }
        }

        public Task ConnectAsync()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            HttpMessageHandler httpMessageHandler = handler;
            if (_useGrpcWeb != null)
            {
                httpMessageHandler = new GrpcWebHandler(_useGrpcWeb.Value, HttpVersion.Version11, httpMessageHandler);
            }

            _client = new HttpClient(httpMessageHandler);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _client?.Dispose();
            return Task.CompletedTask;
        }
    }
}
