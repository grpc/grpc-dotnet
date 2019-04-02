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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.Core;
#if false
using BenchmarkClient.Internal;
using Common;
using Grpc.NetCore.HttpClient;
#endif
using Newtonsoft.Json;

namespace BenchmarkClient
{
    class Program
    {
        private const int Connections = 32;
        private const int DurationSeconds = 20;
        private const string Target = "127.0.0.1:50051";
        private readonly static bool StopOnError = false;
        private readonly static bool LogGrpc = false;

        static async Task Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2Support", true);

            if (LogGrpc)
            {
                Environment.SetEnvironmentVariable("GRPC_VERBOSITY", "DEBUG");
                Environment.SetEnvironmentVariable("GRPC_TRACE", "all");
                GrpcEnvironment.SetLogger(new ConsoleOutLogger());
            }

            var runTasks = new List<Task>();
            var channels = new List<Channel>();
            var channelRequests = new List<int>();

            Log($"Target server: {Target}");

            await CreateChannels(channels, channelRequests);

            Log("Starting benchmark");

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(DurationSeconds));
            cts.Token.Register(() =>
            {
                Log("Benchmark complete");
            });

            for (int i = 0; i < Connections; i++)
            {
                var id = i;
                runTasks.Add(Task.Run(async () =>
                {
                    Log($"{id}: Starting");

                    var requests = 0;
#if false
                    var client = new Greeter.GreeterClient(channels[id]);

                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            var start = DateTime.UtcNow;
                            var response = await client.SayHelloAsync(new HelloRequest
                            {
                                Name = "World"
                            });
                            var end = DateTime.UtcNow;

                            requests++;
                        }
                        catch (Exception ex)
                        {
                            Log($"{id}: Error message: {ex.Message}");
                            if (StopOnError)
                            {
                                cts.Cancel();
                                break;
                            }
                        }
                    }
#else
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;

                    HttpClient client = new HttpClient(handler);

                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            var message = new HelloRequest
                            {
                                Name = "World"
                            };

#if true
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

                            var response = await client.SendAsync(request);
                            response.EnsureSuccessStatusCode();

                            await response.Content.ReadAsByteArrayAsync();
#elif NetcoreGrpcClient
                            var certificate = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? null : new X509Certificate2(Path.Combine(Resources.CertDir, "client.crt"));

                            var c = GrpcClientFactory.Create<Greeter.GreeterClient>("https://" + Target, null);
                            var r = await c.SayHelloAsync(message);
#elif JsonPost
                            var json = JsonConvert.SerializeObject(message);
                            var data = Encoding.UTF8.GetBytes(json);

                            var request = new HttpRequestMessage(HttpMethod.Post, "https://" + Target + "/JsonGreeter");
                            request.Version = new Version(1, 0);
                            request.Content = new StreamContent(new MemoryStream(data));

                            var response = await client.SendAsync(request);
                            var responseContent = await response.Content.ReadAsStringAsync();

                            response.EnsureSuccessStatusCode();
#else
                            var request = new HttpRequestMessage(HttpMethod.Get, "https://" + Target + "/JsonGreeter");
                            request.Version = new Version(2, 0);

                            var response = await client.SendAsync(request);
                            var responseContent = await response.Content.ReadAsStringAsync();

                            response.EnsureSuccessStatusCode();
#endif




                            requests++;
                        }
                        catch (Exception ex)
                        {
                            Log($"{id}: Error message: {ex.ToString()}");
                            if (StopOnError)
                            {
                                cts.Cancel();
                                break;
                            }
                        }
                    }
#endif

                            channelRequests[id] = requests;

                    Log($"{id}: Finished");
                }));
            }

            cts.Token.WaitHandle.WaitOne();

            await Task.WhenAll(runTasks);

            await StopChannels(channels);

            var totalRequests = channelRequests.Sum();

            Log($"Requests per second: {totalRequests / DurationSeconds}");
            Log("Shutting down");
            Log("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task CreateChannels(List<Channel> channels, List<int> requests)
        {
            Log($"Creating channels: {Connections}");

            for (int i = 0; i < Connections; i++)
            {
                var channel = new Channel(Target, ChannelCredentials.Insecure);

                Log($"Connecting channel '{i}'");
                await Task.Delay(0);
                //await channel.ConnectAsync();

                channels.Add(channel);
                requests.Add(0);
            }
        }

        private static async Task StopChannels(List<Channel> channels)
        {
            for (int i = 0; i < Connections; i++)
            {
                await channels[i].ShutdownAsync();
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}