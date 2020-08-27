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
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Testing;
using Microsoft.Extensions.Logging;

namespace GrpcClient
{
    class Program
    {
        private static List<ChannelBase> _channels = null!;
        private static List<object> _locks = null!;
        private static List<int> _requestsPerConnection = null!;
        private static List<int> _errorsPerConnection = null!;
        private static List<List<double>> _latencyPerConnection = null!;
        private static double _maxLatency;
        private static Stopwatch _workTimer = new Stopwatch();
        private static volatile bool _warmingUp;
        private static volatile bool _stopped;
        private static SemaphoreSlim _lock = new SemaphoreSlim(1);
        private static List<(double sum, int count)> _latencyAverage = null!;
        private static int _totalRequests;
        private static ClientOptions _options = null!;
        private static ILoggerFactory? _loggerFactory;
        private static SslCredentials? _credentials;
        private static StringBuilder _errorStringBuilder = new StringBuilder();

        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddOption(new Option<string>(new string[] { "-u", "--url" }, "The server url to request") { Required = true });
            rootCommand.AddOption(new Option<string>(new string[] { "--udsFileName" }, "The Unix Domain Socket file name"));
            rootCommand.AddOption(new Option<int>(new string[] { "-c", "--connections" }, () => 1, "Total number of connections to keep open"));
            rootCommand.AddOption(new Option<int>(new string[] { "-w", "--warmup" }, () => 5, "Duration of the warmup in seconds"));
            rootCommand.AddOption(new Option<int>(new string[] { "-d", "--duration" }, () => 10, "Duration of the test in seconds"));
            rootCommand.AddOption(new Option<string>(new string[] { "-s", "--scenario" }, "Scenario to run") { Required = true });
            rootCommand.AddOption(new Option<bool>(new string[] { "-l", "--latency" }, () => false, "Whether to collect detailed latency"));
            rootCommand.AddOption(new Option<string>(new string[] { "-p", "--protocol" }, "HTTP protocol") { Required = true });
            rootCommand.AddOption(new Option<LogLevel>(new string[] { "-log", "--logLevel" }, () => LogLevel.None, "The log level to use for Console logging"));
            rootCommand.AddOption(new Option<int>(new string[] { "--requestSize" }, "Request payload size"));
            rootCommand.AddOption(new Option<int>(new string[] { "--responseSize" }, "Response payload size"));
            rootCommand.AddOption(new Option<GrpcClientType>(new string[] { "--grpcClientType" }, () => GrpcClientType.GrpcNetClient, "Whether to use Grpc.NetClient or Grpc.Core client"));
            rootCommand.AddOption(new Option<int>(new string[] { "--streams" }, () => 1, "Maximum concurrent streams per connection"));
            rootCommand.AddOption(new Option<bool>(new string[] { "--enableCertAuth" }, () => false, "Flag indicating whether client sends a client certificate"));

            rootCommand.Handler = CommandHandler.Create<ClientOptions>(async (options) =>
            {
                _options = options;

                Log("gRPC Client");

                var runtimeVersion = typeof(object).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
                var isServerGC = GCSettings.IsServerGC;
                var processorCount = Environment.ProcessorCount;

                Log($"NetCoreAppVersion: {runtimeVersion}");
                Log($"{nameof(GCSettings.IsServerGC)}: {isServerGC}");
                Log($"{nameof(Environment.ProcessorCount)}: {processorCount}");

                BenchmarksEventSource.Log.Metadata("NetCoreAppVersion", "first", "first", ".NET Runtime Version", ".NET Runtime Version", "");
                BenchmarksEventSource.Log.Metadata("IsServerGC", "first", "first", "Server GC enabled", "Server GC is enabled", "");
                BenchmarksEventSource.Log.Metadata("ProcessorCount", "first", "first", "Processor Count", "Processor Count", "n0");

                BenchmarksEventSource.Measure("NetCoreAppVersion", runtimeVersion);
                BenchmarksEventSource.Measure("IsServerGC", isServerGC.ToString());
                BenchmarksEventSource.Measure("ProcessorCount", processorCount);

                CreateChannels();

                await StartScenario();

                await StopJobAsync();
            });

            return await rootCommand.InvokeAsync(args);
        }

        private static async Task StartScenario()
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(_options.Duration + _options.Warmup));

            _warmingUp = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.Warmup));
                _workTimer.Restart();
                _warmingUp = false;
                Log("Finished warming up.");
            });

            var callTasks = new List<Task>();

            try
            {
                Log($"Starting {_options.Scenario}");
                Func<int, int, Task> callFactory;

                switch (_options.Scenario?.ToLower())
                {
                    case "unary":
                        callFactory = (connectionId, streamId) => UnaryCall(cts, connectionId, streamId);
                        break;
                    case "serverstreaming":
                        callFactory = (connectionId, streamId) => ServerStreamingCall(cts, connectionId, streamId);
                        break;
                    case "pingpongstreaming":
                        callFactory = (connectionId, streamId) => PingPongStreaming(cts, connectionId, streamId);
                        break;
                    default:
                        throw new Exception($"Scenario '{_options.Scenario}' is not a known scenario.");
                }

                for (var c = 0; c < _channels.Count; c++)
                {
                    for (var s = 0; s < _options.Streams; s++)
                    {
                        var connectionId = c;
                        var streamId = s;
                        var task = Task.Run(() => callFactory(connectionId, streamId));
                        callTasks.Add(task);
                    }
                }

                await Task.WhenAll(callTasks);
            }
            catch (Exception ex)
            {
                var text = "Exception from test: " + ex.Message;
                Log(text);
                _errorStringBuilder.AppendLine();
                _errorStringBuilder.Append($"[{DateTime.Now:hh:mm:ss.fff}] {text}");
            }
        }

        private static async Task StopJobAsync()
        {
            Log($"Stopping client.");
            if (_stopped || !await _lock.WaitAsync(0))
            {
                // someone else is stopping, we only need to do it once
                return;
            }
            try
            {
                _stopped = true;
                _workTimer.Stop();
                CalculateStatistics();
            }
            finally
            {
                _lock.Release();
            }

            BenchmarksEventSource.Log.Metadata("grpc/raw-errors", "all", "all", "Raw errors", "Raw errors", "object");
            BenchmarksEventSource.Measure("grpc/raw-errors", _errorStringBuilder.ToString());
        }

        private static void CalculateStatistics()
        {
            // RPS
            var requestDelta = 0;
            var newTotalRequests = 0;
            var min = int.MaxValue;
            var max = 0;
            for (var i = 0; i < _requestsPerConnection.Count; i++)
            {
                newTotalRequests += _requestsPerConnection[i];

                if (_requestsPerConnection[i] > max)
                {
                    max = _requestsPerConnection[i];
                }
                if (_requestsPerConnection[i] < min)
                {
                    min = _requestsPerConnection[i];
                }
            }

            requestDelta = newTotalRequests - _totalRequests;
            _totalRequests = newTotalRequests;

            // Review: This could be interesting information, see the gap between most active and least active connection
            // Ideally they should be within a couple percent of each other, but if they aren't something could be wrong
            Log($"Least Requests per Connection: {min}");
            Log($"Most Requests per Connection: {max}");

            if (_workTimer.ElapsedMilliseconds <= 0)
            {
                Log("Job failed to run");
                return;
            }

            var rps = (double)requestDelta / _workTimer.ElapsedMilliseconds * 1000;
            var errors = _errorsPerConnection.Sum();
            Log($"RPS: {rps:N0}");
            Log($"Total errors: {errors}");

            BenchmarksEventSource.Log.Metadata("grpc/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/errors/badresponses", "max", "sum", "Bad responses", "Non-2xx or 3xx responses", "n0");

            BenchmarksEventSource.Measure("grpc/rps/max", rps);
            BenchmarksEventSource.Measure("grpc/requests", requestDelta);
            BenchmarksEventSource.Measure("grpc/errors/badresponses", errors);

            // Latency
            CalculateLatency();
        }

        private static void CalculateLatency()
        {
            BenchmarksEventSource.Log.Metadata("grpc/latency/mean", "max", "sum", "Mean latency (ms)", "Mean latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/latency/50", "max", "sum", "50th percentile latency (ms)", "50th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/latency/75", "max", "sum", "75th percentile latency (ms)", "75th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/latency/90", "max", "sum", "90th percentile latency (ms)", "90th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/latency/99", "max", "sum", "99th percentile latency (ms)", "99th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("grpc/latency/max", "max", "sum", "Max latency (ms)", "Max latency (ms)", "n0");
            if (_options.Latency)
            {
                var totalCount = 0;
                var totalSum = 0.0;
                for (var i = 0; i < _latencyPerConnection.Count; i++)
                {
                    for (var j = 0; j < _latencyPerConnection[i].Count; j++)
                    {
                        totalSum += _latencyPerConnection[i][j];
                        totalCount++;
                    }

                    _latencyPerConnection[i].Sort();
                }

                var mean = (totalCount != 0) ? totalSum / totalCount : totalSum;

                BenchmarksEventSource.Measure("grpc/latency/mean", mean);

                var allConnections = new List<double>();
                foreach (var connectionLatency in _latencyPerConnection)
                {
                    allConnections.AddRange(connectionLatency);
                }

                // Review: Each connection can have different latencies, how do we want to deal with that?
                // We could just combine them all and ignore the fact that they are different connections
                // Or we could preserve the results for each one and record them separately
                allConnections.Sort();

                BenchmarksEventSource.Measure("grpc/latency/50", GetPercentile(50, allConnections));
                BenchmarksEventSource.Measure("grpc/latency/75", GetPercentile(75, allConnections));
                BenchmarksEventSource.Measure("grpc/latency/90", GetPercentile(90, allConnections));
                BenchmarksEventSource.Measure("grpc/latency/99", GetPercentile(99, allConnections));
                BenchmarksEventSource.Measure("grpc/latency/max", GetPercentile(100, allConnections));

                Log($"Mean latency: {mean:0.###}ms");
                Log($"Max latency: {GetPercentile(100, allConnections):0.###}ms");
                Log($"50 percentile latency: {GetPercentile(50, allConnections):0.###}ms");
                Log($"75 percentile latency: {GetPercentile(75, allConnections):0.###}ms");
                Log($"90 percentile latency: {GetPercentile(90, allConnections):0.###}ms");
                Log($"99 percentile latency: {GetPercentile(99, allConnections):0.###}ms");
            }
            else
            {
                var totalSum = 0.0;
                var totalCount = 0;
                foreach (var average in _latencyAverage)
                {
                    totalSum += average.sum;
                    totalCount += average.count;
                }

                var mean = (totalCount != 0) ? totalSum / totalCount : totalSum;

                BenchmarksEventSource.Measure("grpc/latency/mean", mean);
                BenchmarksEventSource.Measure("grpc/latency/max", _maxLatency);

                Log($"Mean latency: {mean:0.###}ms");
                Log($"Max latency: {_maxLatency:0.###}ms");
            }
        }

        private static double GetPercentile(int percent, List<double> sortedData)
        {
            if (percent == 100)
            {
                return sortedData[sortedData.Count - 1];
            }

            var i = ((long)percent * sortedData.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedData[(int)Math.Truncate(i) - 1] + fractionPart * sortedData[(int)Math.Ceiling(i) - 1];
        }

        private static void CreateChannels()
        {
            _channels = new List<ChannelBase>(_options.Connections);
            _locks = new List<object>(_options.Connections);
            _requestsPerConnection = new List<int>(_options.Connections);
            _errorsPerConnection = new List<int>(_options.Connections);
            _latencyPerConnection = new List<List<double>>(_options.Connections);
            _latencyAverage = new List<(double sum, int count)>(_options.Connections);

            if (_options.LogLevel != LogLevel.None)
            {
                _loggerFactory = LoggerFactory.Create(c =>
                {
                    c.AddConsole();
                    c.SetMinimumLevel(_options.LogLevel);
                });
            }

            // Channel does not care about scheme
            var initialUri = new Uri(_options.Url!);
            var resolvedUri = initialUri.Authority;

            Log($"gRPC client type: {_options.GrpcClientType}");
            Log($"Log level: {_options.LogLevel}");
            Log($"Protocol: '{_options.Protocol}'");
            Log($"Creating channels to '{resolvedUri}'");

            for (var i = 0; i < _options.Connections; i++)
            {
                _requestsPerConnection.Add(0);
                _errorsPerConnection.Add(0);
                _latencyPerConnection.Add(new List<double>());
                _latencyAverage.Add((0, 0));

                var channel = CreateChannel(resolvedUri);
                _channels.Add(channel);

                _locks.Add(new object());
            }
        }

        private static ChannelBase CreateChannel(string target)
        {
            var useTls = _options.Protocol == "h2";

            switch (_options.GrpcClientType)
            {
                default:
                case GrpcClientType.GrpcCore:
                    if (_options.EnableCertAuth)
                    {
                        throw new Exception("Client certificate not implemented for Grpc.Core");
                    }

                    var channelCredentials = useTls ? GetSslCredentials() : ChannelCredentials.Insecure;

                    var channel = new Channel(target, channelCredentials);
                    return channel;
                case GrpcClientType.GrpcNetClient:
                    var address = useTls ? "https://" : "http://";
                    address += target;

                    // This switch must be set before creating the GrpcChannel/HttpClient.
                    // It allows HttpClient to make HTTP/2 calls without TLS.
                    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                    var httpClientHandler = new SocketsHttpHandler();
                    httpClientHandler.UseProxy = false;
                    httpClientHandler.AllowAutoRedirect = false;
                    if (_options.EnableCertAuth)
                    {
                        var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                        var certPath = Path.Combine(basePath!, "Certs", "client.pfx");
                        var clientCertificate = new X509Certificate2(certPath, "1111");
                        httpClientHandler.SslOptions.ClientCertificates = new X509CertificateCollection
                        {
                            clientCertificate
                        };
                    }
                    if (!string.IsNullOrEmpty(_options.UdsFileName))
                    {
                        httpClientHandler.ConnectionFactory = new UnixDomainSocketConnectionFactory(new UnixDomainSocketEndPoint(ResolveUdsPath(_options.UdsFileName)));
                    }

                    // TODO(JamesNK): Check whether the disable can be removed once .NET 5 is finalized
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
                    httpClientHandler.SslOptions.RemoteCertificateValidationCallback = (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true;
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

                    return GrpcChannel.ForAddress(address, new GrpcChannelOptions
                    {
                        HttpHandler = httpClientHandler,
                        LoggerFactory = _loggerFactory
                    });
            }
        }

        private static string ResolveUdsPath(string udsFileName) => Path.Combine(Path.GetTempPath(), udsFileName);

        private static SslCredentials GetSslCredentials()
        {
            if (_credentials == null)
            {
                Log($"Loading credentials from '{AppContext.BaseDirectory}'");

                _credentials = new SslCredentials(
                    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "ca.crt")),
                    new KeyCertificatePair(
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "client.crt")),
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "client.key"))));
            }

            return _credentials;
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        private static void Log(int connectionId, int streamId, string message)
        {
            Log($"{connectionId} {streamId}: {message}");
        }

        private static SimpleRequest CreateSimpleRequest()
        {
            return new SimpleRequest
            {
                Payload = new Payload { Body = ByteString.CopyFrom(new byte[_options.RequestSize]) },
                ResponseSize = _options.ResponseSize
            };
        }

        private static void ReceivedDateTime(DateTime start, DateTime end, int connectionId)
        {
            if (_stopped || _warmingUp)
            {
                return;
            }

            lock (_locks[connectionId])
            {
                _requestsPerConnection[connectionId] += 1;

                var latency = end - start;
                if (_options.Latency)
                {
                    _latencyPerConnection[connectionId].Add(latency.TotalMilliseconds);
                }
                else
                {
                    var (sum, count) = _latencyAverage[connectionId];
                    sum += latency.TotalMilliseconds;
                    count++;
                    _latencyAverage[connectionId] = (sum, count);
                    _maxLatency = Math.Max(latency.TotalMilliseconds, _maxLatency);
                }
            }
        }

        private static void HandleError(int connectionId)
        {
            if (_stopped || _warmingUp)
            {
                return;
            }

            lock (_locks[connectionId])
            {
                _errorsPerConnection[connectionId] = _errorsPerConnection[connectionId] + 1;
            }
        }

        private static async Task PingPongStreaming(CancellationTokenSource cts, int connectionId, int streamId)
        {
            Log(connectionId, streamId, $"Starting {_options.Scenario}");

            var client = new BenchmarkService.BenchmarkServiceClient(_channels[connectionId]);
            var request = CreateSimpleRequest();
            using var call = client.StreamingCall();

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var start = DateTime.UtcNow;
                    await call.RequestStream.WriteAsync(request);
                    if (!await call.ResponseStream.MoveNext())
                    {
                        throw new Exception("Unexpected end of stream.");
                    }
                    var end = DateTime.UtcNow;

                    ReceivedDateTime(start, end, connectionId);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cts.IsCancellationRequested)
                {
                    // Handle expected error from canceling call
                    break;
                }
                catch (Exception ex)
                {
                    HandleError(connectionId);

                    Log(connectionId, streamId, $"Error message: {ex}");
                }
            }

            Log($"{connectionId}: Completing request stream");
            await call.RequestStream.CompleteAsync();

            Log(connectionId, streamId, $"Finished {_options.Scenario}");
        }

        private static async Task ServerStreamingCall(CancellationTokenSource cts, int connectionId, int streamId)
        {
            Log(connectionId, streamId, $"Starting {_options.Scenario}");

            var client = new BenchmarkService.BenchmarkServiceClient(_channels[connectionId]);
            using var call = client.StreamingFromServer(CreateSimpleRequest(), cancellationToken: cts.Token);

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var start = DateTime.UtcNow;
                    if (!await call.ResponseStream.MoveNext())
                    {
                        throw new Exception("Unexpected end of stream.");
                    }
                    var end = DateTime.UtcNow;

                    ReceivedDateTime(start, end, connectionId);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cts.IsCancellationRequested)
                {
                    // Handle expected error from canceling call
                    break;
                }
                catch (Exception ex)
                {
                    HandleError(connectionId);

                    Log(connectionId, streamId, $"Error message: {ex}");
                }
            }

            Log(connectionId, streamId, $"Finished {_options.Scenario}");
        }

        private static async Task UnaryCall(CancellationTokenSource cts, int connectionId, int streamId)
        {
            Log(connectionId, streamId, $"Starting {_options.Scenario}");

            var client = new BenchmarkService.BenchmarkServiceClient(_channels[connectionId]);

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var start = DateTime.UtcNow;
                    var response = await client.UnaryCallAsync(CreateSimpleRequest());
                    var end = DateTime.UtcNow;

                    ReceivedDateTime(start, end, connectionId);
                }
                catch (Exception ex)
                {
                    HandleError(connectionId);

                    Log(connectionId, streamId, $"Error message: {ex}");
                }
            }

            Log(connectionId, streamId, $"Finished {_options.Scenario}");
        }
    }
}