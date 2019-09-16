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
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Grpc.Testing;
using Newtonsoft.Json;

namespace BenchmarkClient.Worker
{
    public class JsonWorker : IWorker
    {
        private readonly bool _useClientCertificate;
        private readonly string _path;
        private HttpClient? _client;

        public JsonWorker(int id, string target, bool useClientCertificate, string path)
        {
            Id = id;
            Target = target;
            _useClientCertificate = useClientCertificate;
            _path = path;
        }

        public int Id { get; }
        public string Target { get; }

        public async Task CallAsync()
        {
            var message = new SimpleRequest
            {
                ResponseSize = 10
            };

            var json = JsonConvert.SerializeObject(message);
            var data = Encoding.UTF8.GetBytes(json);

            var url = _useClientCertificate ? "https://" : "http://";
            url += Target;

            var request = new HttpRequestMessage(HttpMethod.Post, url + _path);
            request.Version = new Version(2, 0);
            request.Content = new StreamContent(new MemoryStream(data));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _client!.SendAsync(request);

            var content = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();
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
