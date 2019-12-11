﻿#region Copyright notice and license

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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkClient.ChannelFactory;
using BenchmarkClient.Worker;

namespace BenchmarkClient
{
    class Program
    {
        private const int Connections = 8;
        private const int DurationSeconds = 5;
        private const bool UseTls = false;
        private const bool UseClientCertificate = false;
        // The host name is tied to some certificates
        private const string Target = "localhost:5000";
        private readonly static bool StopOnError = false;

        static async Task Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var benchmarkResults = new List<BenchmarkResult>();

            var grpcNetClientChannelFactory = new GrpcNetClientChannelFactory(Target, UseTls, UseClientCertificate);
            var grpcCoreChannelFactory = new GrpcCoreChannelFactory(Target);

            benchmarkResults.Add(await ExecuteBenchmark("GrpcRaw-UnaryWorker", id => new GrpcRawUnaryWorker(id, Target, UseTls)));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcNetClient-UnaryWorker", id => new GrpcUnaryWorker(id, grpcNetClientChannelFactory)));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcNetClient-PingPongStreamingWorker", id => new GrpcPingPongStreamingWorker(id, grpcNetClientChannelFactory)));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcNetClient-ServerStreamingWorker", id => new GrpcServerStreamingWorker(id, grpcNetClientChannelFactory)));
            benchmarkResults.Add(await ExecuteBenchmark("JsonRaw", id => new JsonWorker(id, Target, UseTls, "/unary")));
            benchmarkResults.Add(await ExecuteBenchmark("JsonMvc", id => new JsonWorker(id, Target, UseTls, "/api/benchmark/unary")));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcCore-UnaryWorker", id => new GrpcUnaryWorker(id, grpcCoreChannelFactory)));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcCore-ServerStreamingWorker", id => new GrpcServerStreamingWorker(id, grpcCoreChannelFactory)));
            benchmarkResults.Add(await ExecuteBenchmark("GrpcCore-PingPongStreamingWorker", id => new GrpcPingPongStreamingWorker(id, grpcCoreChannelFactory)));

            Log($"Results:");

            foreach (var result in benchmarkResults)
            {
                Log($"{result.Name} request: {result.RequestCount}");
            }

            Log("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task<BenchmarkResult> ExecuteBenchmark(string name, Func<int, IWorker> workerFactory)
        {
            var runTasks = new List<Task>();
            var workers = new List<IWorker>();
            var workerRequests = new List<int>();

            Log($"Setting up benchmark '{name}'");

            await CreateWorkers(workers, workerFactory, workerRequests);

            Log($"Starting benchmark '{name}'");

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(DurationSeconds));
            cts.Token.Register(() =>
            {
                Log($"Benchmark complete '{name}'");
            });

            for (var i = 0; i < Connections; i++)
            {
                var id = i;
                var worker = workers[i];
                runTasks.Add(Task.Run(async () =>
                {
                    Log($"{name} {id}: Starting");

                    var requests = 0;

                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            await worker.CallAsync();

                            requests++;
                        }
                        catch (Exception ex)
                        {
                            Log($"{name} {id}: Error message: {ex.Message}");
                            if (StopOnError)
                            {
                                cts.Cancel();
                                break;
                            }
                        }
                    }

                    workerRequests[id] = requests;

                    Log($"{name} {id}: Finished");
                }));
            }

            cts.Token.WaitHandle.WaitOne();

            await Task.WhenAll(runTasks);

            await StopWorkers(workers);

            var totalRequests = workerRequests.Sum();

            return new BenchmarkResult
            {
                Name = name,
                RequestCount = totalRequests
            };
        }

        private static async Task CreateWorkers(List<IWorker> workers, Func<int, IWorker> workerFactory, List<int> requests)
        {
            Log($"Creating workers: {Connections}");

            for (var i = 0; i < Connections; i++)
            {
                var worker = workerFactory(i);
                await worker.ConnectAsync();

                workers.Add(worker);
                requests.Add(0);
            }
        }

        private static async Task StopWorkers(List<IWorker> workers)
        {
            for (var i = 0; i < Connections; i++)
            {
                await workers[i].DisconnectAsync();
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}