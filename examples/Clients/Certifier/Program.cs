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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace Sample.Clients
{
    class Program
    {
        private const string Address = "localhost:50051";

        static async Task Main(string[] args)
        {
            // The server will return 403 (Forbidden). The method requires a certificate
            await CallCertificateInfo(includeClientCertificate: false);

            // The server will return a successful gRPC response
            await CallCertificateInfo(includeClientCertificate: true);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task CallCertificateInfo(bool includeClientCertificate)
        {
            try
            {
                Console.WriteLine($"Setting up HttpClient. Client has certificate: {includeClientCertificate}");
                var httpClient = CreateHttpClient(includeClientCertificate);
                var channelBuilder = ChannelBuilder.ForHttpClient(httpClient);
                var client = new Certifier.CertifierClient(channelBuilder.Build());

                Console.WriteLine("Sending gRPC call...");
                var certificateInfo = await client.GetCertificateInfoAsync(new Empty());

                Console.WriteLine($"Server received client certificate: {certificateInfo.HasCertificate}");
                if (certificateInfo.HasCertificate)
                {
                    Console.WriteLine($"Client certificate name: {certificateInfo.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling service: {ex.Message}");
            }
        }

        private static HttpClient CreateHttpClient(bool includeClientCertificate)
        {
            var handler = new HttpClientHandler();

            if (includeClientCertificate)
            {
                // Load client certificate
                var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                var certPath = Path.Combine(basePath!, "Certs", "client.pfx");
                var clientCertificate = new X509Certificate2(certPath, "1111");
                handler.ClientCertificates.Add(clientCertificate);
            }

            // Create client
            var httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri($"https://{Address}");

            return httpClient;
        }
    }
}
