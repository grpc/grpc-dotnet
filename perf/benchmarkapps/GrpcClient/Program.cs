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

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Testing;
using Grpc.Tests.Shared;
using Microsoft.Crank.EventSources;
using Microsoft.Extensions.Logging;

namespace GrpcClient;

class Program
{
    private static List<ChannelBase> _channels = null!;
    private static List<object> _locks = null!;
    private static List<int> _requestsPerConnection = null!;
    private static List<int> _errorsPerConnection = null!;
    private static List<List<double>> _latencyPerConnection = null!;
    private static int _callsStarted;
    private static double _maxLatency;
    private static double _firstRequestLatency;
    private static readonly Stopwatch _workTimer = new Stopwatch();
    private static volatile bool _warmingUp;
    private static volatile bool _stopped;
    private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1);
    private static List<(double sum, int count)> _latencyAverage = null!;
    private static int _totalRequests;
    private static ClientOptions _options = null!;
    private static ILoggerFactory? _loggerFactory;
    private static SslCredentials? _credentials;
    private static readonly StringBuilder _errorStringBuilder = new StringBuilder();
    private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public static async Task<int> Main(string[] args)
    {
        var urlOption = new Option<Uri>(new string[] { "-u", "--url" }, "The server url to request") { IsRequired = true };
        var udsFileNameOption = new Option<string>(new string[] { "--udsFileName" }, "The Unix Domain Socket file name");
        var namedPipeNameOption = new Option<string>(new string[] { "--namedPipeName" }, "The Named Pipe name");
        var connectionsOption = new Option<int>(new string[] { "-c", "--connections" }, () => 1, "Total number of connections to keep open");
        var warmupOption = new Option<int>(new string[] { "-w", "--warmup" }, () => 5, "Duration of the warmup in seconds");
        var durationOption = new Option<int>(new string[] { "-d", "--duration" }, () => 10, "Duration of the test in seconds");
        var callCountOption = new Option<int?>(new string[] { "--callCount" }, "Call count of test");
        var scenarioOption = new Option<string>(new string[] { "-s", "--scenario" }, "Scenario to run") { IsRequired = true };
        var latencyOption = new Option<bool>(new string[] { "-l", "--latency" }, () => false, "Whether to collect detailed latency");
        var protocolOption = new Option<string>(new string[] { "-p", "--protocol" }, "HTTP protocol") { IsRequired = true };
        var logOption = new Option<LogLevel>(new string[] { "-log", "--logLevel" }, () => LogLevel.None, "The log level to use for Console logging");
        var requestSizeOption = new Option<int>(new string[] { "--requestSize" }, "Request payload size");
        var responseSizeOption = new Option<int>(new string[] { "--responseSize" }, "Response payload size");
        var grpcClientTypeOption = new Option<GrpcClientType>(new string[] { "--grpcClientType" }, () => GrpcClientType.GrpcNetClient, "Whether to use Grpc.NetClient or Grpc.Core client");
        var streamsOption = new Option<int>(new string[] { "--streams" }, () => 1, "Maximum concurrent streams per connection");
        var enableCertAuthOption = new Option<bool>(new string[] { "--enableCertAuth" }, () => false, "Flag indicating whether client sends a client certificate");
        var deadlineOption = new Option<int>(new string[] { "--deadline" }, "Duration of deadline in seconds");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(urlOption);
        rootCommand.AddOption(udsFileNameOption);
        rootCommand.AddOption(namedPipeNameOption);
        rootCommand.AddOption(connectionsOption);
        rootCommand.AddOption(warmupOption);
        rootCommand.AddOption(durationOption);
        rootCommand.AddOption(callCountOption);
        rootCommand.AddOption(scenarioOption);
        rootCommand.AddOption(latencyOption);
        rootCommand.AddOption(protocolOption);
        rootCommand.AddOption(logOption);
        rootCommand.AddOption(requestSizeOption);
        rootCommand.AddOption(responseSizeOption);
        rootCommand.AddOption(grpcClientTypeOption);
        rootCommand.AddOption(streamsOption);
        rootCommand.AddOption(enableCertAuthOption);
        rootCommand.AddOption(deadlineOption);

        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            _options = new ClientOptions();
            _options.Url = context.ParseResult.GetValueForOption(urlOption);
            _options.UdsFileName = context.ParseResult.GetValueForOption(udsFileNameOption);
            _options.NamedPipeName = context.ParseResult.GetValueForOption(namedPipeNameOption);
            _options.Connections = context.ParseResult.GetValueForOption(connectionsOption);
            _options.Warmup = context.ParseResult.GetValueForOption(warmupOption);
            _options.Duration = context.ParseResult.GetValueForOption(durationOption);
            _options.CallCount = context.ParseResult.GetValueForOption(callCountOption);
            _options.Scenario = context.ParseResult.GetValueForOption(scenarioOption);
            _options.Latency = context.ParseResult.GetValueForOption(latencyOption);
            _options.Protocol = context.ParseResult.GetValueForOption(protocolOption);
            _options.LogLevel = context.ParseResult.GetValueForOption(logOption);
            _options.RequestSize = context.ParseResult.GetValueForOption(requestSizeOption);
            _options.ResponseSize = context.ParseResult.GetValueForOption(responseSizeOption);
            _options.GrpcClientType = context.ParseResult.GetValueForOption(grpcClientTypeOption);
            _options.Streams = context.ParseResult.GetValueForOption(streamsOption);
            _options.EnableCertAuth = context.ParseResult.GetValueForOption(enableCertAuthOption);
            _options.Deadline = context.ParseResult.GetValueForOption(deadlineOption);

            var runtimeVersion = typeof(object).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
            var isServerGC = GCSettings.IsServerGC;
            var processorCount = Environment.ProcessorCount;

            Log($"NetCoreAppVersion: {runtimeVersion}");
            Log($"{nameof(GCSettings.IsServerGC)}: {isServerGC}");
            Log($"{nameof(Environment.ProcessorCount)}: {processorCount}");

            BenchmarksEventSource.Register("NetCoreAppVersion", Operations.First, Operations.First, ".NET Runtime Version", ".NET Runtime Version", "");
            BenchmarksEventSource.Register("IsServerGC", Operations.First, Operations.First, "Server GC enabled", "Server GC is enabled", "");
            BenchmarksEventSource.Register("ProcessorCount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");

            BenchmarksEventSource.Measure("NetCoreAppVersion", runtimeVersion);
            BenchmarksEventSource.Measure("IsServerGC", isServerGC.ToString());
            BenchmarksEventSource.Measure("ProcessorCount", processorCount);

            HttpEventSourceListener? listener = null;
            if (_options.LogLevel != LogLevel.None)
            {
                Log($"Setting up logging. Log level: {_options.LogLevel}");

                _loggerFactory = CreateLoggerFactory();

                listener = new HttpEventSourceListener(_loggerFactory);
            }

            CreateChannels();

            await StartScenario();

            await StopJobAsync();

            listener?.Dispose();
        });

        Log("gRPC Client");

        return await rootCommand.InvokeAsync(args);
    }

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
        Justification = "DependencyInjection only used with safe types.")]
    private static ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(c =>
        {
            c.AddConsole();
            c.SetMinimumLevel(_options.LogLevel);
        });
    }

    private static async Task StartScenario()
    {
        if (_options.CallCount == null)
        {
            Log("Warm up: " + _options.Warmup);
            Log("Duration: " + _options.Duration);

            _cts.Token.Register(() =>
            {
                if (IsCallCountExceeded())
                {
                    Log($"Reached call count {_options.CallCount}.");
                }
                else
                {
                    Log($"Reached duration {_options.Duration}.");
                }
            });
            _cts.CancelAfter(TimeSpan.FromSeconds(_options.Duration + _options.Warmup));

            _warmingUp = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.Warmup));
                _workTimer.Restart();
                _warmingUp = false;
                Log("Finished warming up.");
            });
        }
        else
        {
            Log("Call count: " + _options.CallCount);
        }

        var callTasks = new List<Task>();

        try
        {
            Log($"Starting {_options.Scenario}");
            Func<int, int, Task> callFactory;

            switch (_options.Scenario?.ToLower())
            {
                case "unary":
                    callFactory = (connectionId, streamId) => UnaryCall(_cts, connectionId, streamId);
                    break;
                case "serverstreaming":
                    callFactory = (connectionId, streamId) => ServerStreamingCall(_cts, connectionId, streamId);
                    break;
                case "pingpongstreaming":
                    callFactory = (connectionId, streamId) => PingPongStreaming(_cts, connectionId, streamId);
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
            _errorStringBuilder.Append(CultureInfo.InvariantCulture, $"[{DateTime.Now:hh:mm:ss.fff}] {text}");
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

        BenchmarksEventSource.Register("grpc/raw-errors", Operations.All, Operations.All, "Raw errors", "Raw errors", "object");
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
        
        Log($"First request: {_firstRequestLatency:0.###}ms");

        // Review: This could be interesting information, see the gap between most active and least active connection
        // Ideally they should be within a couple percent of each other, but if they aren't something could be wrong
        Log($"Least Requests per Connection: {min}");
        Log($"Most Requests per Connection: {max}");

        if (_options.CallCount == null && _workTimer.ElapsedMilliseconds <= 0)
        {
            Log("Job failed to run");
            return;
        }

        var rps = (double)requestDelta / _workTimer.ElapsedMilliseconds * 1000;
        var errors = _errorsPerConnection.Sum();
        Log($"RPS: {rps:N0}");
        Log($"Total requests: {requestDelta}");
        Log($"Total errors: {errors}");

        BenchmarksEventSource.Register("grpc/firstrequest", Operations.Max, Operations.Max, "First Request (ms)", "Time to first request in ms", "n2");
        BenchmarksEventSource.Register("grpc/rps/mean;http/rps/mean", Operations.Max, Operations.Sum, "Requests/sec", "Requests per second", "n0");
        BenchmarksEventSource.Register("grpc/requests;http/requests", Operations.Max, Operations.Sum, "Requests", "Total number of requests", "n0");
        BenchmarksEventSource.Register("grpc/errors/badresponses;http/requests/badresponses", Operations.Max, Operations.Sum, "Bad responses", "Non-2xx or 3xx responses", "n0");

        BenchmarksEventSource.Measure("grpc/firstrequest", _firstRequestLatency);
        BenchmarksEventSource.Measure("grpc/rps/mean;http/rps/mean", rps);
        BenchmarksEventSource.Measure("grpc/requests;http/requests", requestDelta);
        BenchmarksEventSource.Measure("grpc/errors/badresponses;http/requests/badresponses", errors);

        // Latency
        CalculateLatency();
    }

    private static void CalculateLatency()
    {
        BenchmarksEventSource.Register("grpc/latency/mean;http/latency/mean", Operations.Max, Operations.Max, "Mean latency (ms)", "Mean latency (ms)", "n2");
        BenchmarksEventSource.Register("grpc/latency/50;http/latency/50", Operations.Max, Operations.Max, "50th percentile latency (ms)", "50th percentile latency (ms)", "n2");
        BenchmarksEventSource.Register("grpc/latency/75;http/latency/75", Operations.Max, Operations.Max, "75th percentile latency (ms)", "75th percentile latency (ms)", "n2");
        BenchmarksEventSource.Register("grpc/latency/90;http/latency/90", Operations.Max, Operations.Max, "90th percentile latency (ms)", "90th percentile latency (ms)", "n2");
        BenchmarksEventSource.Register("grpc/latency/99;http/latency/99", Operations.Max, Operations.Max, "99th percentile latency (ms)", "99th percentile latency (ms)", "n2");
        BenchmarksEventSource.Register("grpc/latency/max;http/latency/max", Operations.Max, Operations.Max, "Max latency (ms)", "Max latency (ms)", "n2");
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

            BenchmarksEventSource.Measure("grpc/latency/mean;http/latency/mean", mean);

            var allConnections = new List<double>();
            foreach (var connectionLatency in _latencyPerConnection)
            {
                allConnections.AddRange(connectionLatency);
            }

            // Review: Each connection can have different latencies, how do we want to deal with that?
            // We could just combine them all and ignore the fact that they are different connections
            // Or we could preserve the results for each one and record them separately
            allConnections.Sort();

            BenchmarksEventSource.Measure("grpc/latency/50;http/latency/50", GetPercentile(50, allConnections));
            BenchmarksEventSource.Measure("grpc/latency/75;http/latency/75", GetPercentile(75, allConnections));
            BenchmarksEventSource.Measure("grpc/latency/90;http/latency/90", GetPercentile(90, allConnections));
            BenchmarksEventSource.Measure("grpc/latency/99;http/latency/99", GetPercentile(99, allConnections));
            BenchmarksEventSource.Measure("grpc/latency/max;http/latency/max", GetPercentile(100, allConnections));

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

            BenchmarksEventSource.Measure("grpc/latency/mean;http/latency/mean", mean);
            BenchmarksEventSource.Measure("grpc/latency/max;http/latency/max", _maxLatency);

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

        // Channel does not care about scheme
        var initialUri = _options.Url!;
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
        var useTls = _options.Protocol == "h2" || _options.Protocol == "h3";

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

                var httpClientHandler = new SocketsHttpHandler();
                httpClientHandler.UseProxy = false;
                httpClientHandler.AllowAutoRedirect = false;
                if (_options.EnableCertAuth)
                {
                    var basePath = Path.GetDirectoryName(AppContext.BaseDirectory);
                    var certPath = Path.Combine(basePath!, "Certs", "client.pfx");
                    var clientCertificates = X509CertificateLoader.LoadPkcs12CollectionFromFile(certPath, "1111");
                    httpClientHandler.SslOptions.ClientCertificates = clientCertificates;
                }

                if (!string.IsNullOrEmpty(_options.UdsFileName))
                {
                    var connectionFactory = new UnixDomainSocketConnectionFactory(new UnixDomainSocketEndPoint(ResolveUdsPath(_options.UdsFileName)));
                    httpClientHandler.ConnectCallback = connectionFactory.ConnectAsync;
                }
                else if (!string.IsNullOrEmpty(_options.NamedPipeName))
                {
                    var connectionFactory = new NamedPipeConnectionFactory(_options.NamedPipeName);
                    httpClientHandler.ConnectCallback = connectionFactory.ConnectAsync;
                }

                httpClientHandler.SslOptions.RemoteCertificateValidationCallback =
                    (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true;

                HttpMessageHandler httpMessageHandler = httpClientHandler;

                Version? versionOverride = null;
                if (_options.Protocol == "h3")
                {
                    // Stop gRPC channel from creating TCP socket.
                    httpClientHandler.ConnectCallback = (context, cancellationToken) => throw new InvalidOperationException("Should never be called for H3.");

                    // Force H3 on all requests.
                    versionOverride = new Version(3, 0);
                }

                return GrpcChannel.ForAddress(address, new GrpcChannelOptions
                {
                    HttpHandler = httpMessageHandler,
                    LoggerFactory = _loggerFactory,
                    HttpVersion = versionOverride
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
        var time = DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.InvariantCulture);
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
            Payload = new Payload { Body = UnsafeByteOperations.UnsafeWrap(new byte[_options.RequestSize]) },
            ResponseSize = _options.ResponseSize
        };
    }

    private static void ReceivedDateTime(DateTime start, DateTime end, int connectionId)
    {
        var latency = (end - start).TotalMilliseconds;
        
        // Update first request latency with the first non-zero value.
        Interlocked.CompareExchange(ref _firstRequestLatency, latency, 0d);
        
        if (_stopped || _warmingUp)
        {
            return;
        }

        lock (_locks[connectionId])
        {
            _requestsPerConnection[connectionId] += 1;

            if (_options.Latency)
            {
                _latencyPerConnection[connectionId].Add(latency);
            }
            else
            {
                var (sum, count) = _latencyAverage[connectionId];
                sum += latency;
                count++;
                _latencyAverage[connectionId] = (sum, count);
                _maxLatency = Math.Max(latency, _maxLatency);
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

    private static CallOptions CreateCallOptions()
    {
        var callOptions = new CallOptions();
        if (_options.Deadline > 0)
        {
            callOptions = callOptions.WithDeadline(DateTime.UtcNow.AddSeconds(_options.Deadline));
        }

        return callOptions;
    }

    private static async Task PingPongStreaming(CancellationTokenSource cts, int connectionId, int streamId)
    {
        Log(connectionId, streamId, $"Starting {_options.Scenario}");

        var client = new BenchmarkService.BenchmarkServiceClient(_channels[connectionId]);
        var request = CreateSimpleRequest();
        var callOptions = CreateCallOptions();
        callOptions = callOptions.WithCancellationToken(cts.Token);
        using var call = client.StreamingCall(callOptions);

        while (!cts.IsCancellationRequested)
        {
            if (StartCall())
            {
                break;
            }

            var start = DateTime.UtcNow;
            try
            {
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
                var end = DateTime.UtcNow;
                ReceivedDateTime(start, end, connectionId);

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
        var callOptions = CreateCallOptions();
        callOptions = callOptions.WithCancellationToken(cts.Token);
        using var call = client.StreamingFromServer(CreateSimpleRequest(), callOptions);

        while (!cts.IsCancellationRequested)
        {
            if (StartCall())
            {
                break;
            }

            var start = DateTime.UtcNow;
            try
            {
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
                var end = DateTime.UtcNow;
                ReceivedDateTime(start, end, connectionId);

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
            if (StartCall())
            {
                break;
            }

            var start = DateTime.UtcNow;
            try
            {
                var response = await client.UnaryCallAsync(CreateSimpleRequest(), CreateCallOptions());

                var end = DateTime.UtcNow;
                ReceivedDateTime(start, end, connectionId);
            }
            catch (Exception ex)
            {
                var end = DateTime.UtcNow;
                ReceivedDateTime(start, end, connectionId);

                HandleError(connectionId);

                Log(connectionId, streamId, $"Error message: {ex}");
            }
        }

        Log(connectionId, streamId, $"Finished {_options.Scenario}");
    }

    private static bool StartCall()
    {
        Interlocked.Increment(ref _callsStarted);
        if (IsCallCountExceeded())
        {
            _cts.Cancel();
            return true;
        }

        return false;
    }

    private static bool IsCallCountExceeded()
    {
        return _options.CallCount != null && _callsStarted > _options.CallCount;
    }
}
