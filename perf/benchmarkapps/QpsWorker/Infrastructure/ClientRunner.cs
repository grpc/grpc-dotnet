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

using System.Diagnostics;
using System.Net.Security;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Utils;
using Grpc.Net.Client;
using Grpc.Testing;
using Microsoft.Extensions.Logging;

namespace QpsWorker.Infrastructure;

/// <summary>
/// Helper methods to start client runners for performance testing.
/// </summary>
public class ClientRunner
{
    private const double SecondsToNanos = 1e9;

    private readonly List<GrpcChannel> _channels;
    private readonly ClientType _clientType;
    private readonly RpcType _rpcType;
    private readonly PayloadConfig _payloadConfig;
    private readonly ILogger _logger;
    private readonly Lazy<byte[]> _cachedByteBufferRequest;
    private readonly ThreadLocal<Histogram> _threadLocalHistogram;

    private readonly List<Task> _runnerTasks;
    private readonly CancellationTokenSource _stoppedCts = new CancellationTokenSource();
    private readonly TimeStats _timeStats = new TimeStats();
    private readonly AtomicCounter _statsResetCount = new AtomicCounter();

    /// <summary>
    /// Creates a started client runner.
    /// </summary>
    public static ClientRunner Start(ILoggerFactory loggerFactory, ClientConfig config)
    {
        var logger = loggerFactory.CreateLogger<ClientRunner>();

        logger.LogDebug("ClientConfig: {0}", config);

        if (config.AsyncClientThreads != 0)
        {
            logger.LogWarning("ClientConfig.AsyncClientThreads is not supported for C#. Ignoring the value");
        }
        if (config.CoreLimit != 0)
        {
            logger.LogWarning("ClientConfig.CoreLimit is not supported for C#. Ignoring the value");
        }
        if (config.CoreList.Count > 0)
        {
            logger.LogWarning("ClientConfig.CoreList is not supported for C#. Ignoring the value");
        }

        var configRoot = ConfigHelpers.GetConfiguration();

        var clientLoggerFactory = LoggerFactory.Create(builder =>
        {
            if (Enum.TryParse<LogLevel>(configRoot["LogLevel"], out var logLevel) && logLevel != LogLevel.None)
            {
                logger.LogInformation($"Console Logging enabled with level '{logLevel}'");
                builder.AddSimpleConsole(o => o.TimestampFormat = "ss.ffff ").SetMinimumLevel(logLevel);
            }
        });

        var channels = CreateChannels(
            config.ClientChannels,
            config.ServerTargets,
            config.SecurityParams,
            clientLoggerFactory);

        return new ClientRunner(channels,
            config.ClientType,
            config.RpcType,
            config.OutstandingRpcsPerChannel,
            config.LoadParams,
            config.PayloadConfig,
            config.HistogramParams,
            logger);
    }

    private static List<GrpcChannel> CreateChannels(
        int clientChannels,
        IEnumerable<string> serverTargets,
        SecurityParams securityParams,
        ILoggerFactory clientLoggerFactory)
    {
        GrpcPreconditions.CheckArgument(clientChannels > 0, "clientChannels needs to be at least 1.");
        GrpcPreconditions.CheckArgument(serverTargets.Count() > 0, "at least one serverTarget needs to be specified.");

        var result = new List<GrpcChannel>();
        for (var i = 0; i < clientChannels; i++)
        {
            var target = serverTargets.ElementAt(i % serverTargets.Count());

            // Contents of "securityParams" (useTestCa and sslTargetHostOverride) are basically ignored.
            // Instead the client just uses TLS and disable any certificate checks.
            var prefix = (securityParams == null) ? "http://" : "https://";
            var channel = GrpcChannel.ForAddress(prefix + target, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        // Ignore TLS certificate errors.
                        RemoteCertificateValidationCallback = (_, __, ___, ____) => true
                    }
                },
                LoggerFactory = clientLoggerFactory
            });
            result.Add(channel);
        }
        return result;
    }

    public ClientRunner(List<GrpcChannel> channels, ClientType clientType, RpcType rpcType, int outstandingRpcsPerChannel, LoadParams loadParams, PayloadConfig payloadConfig, HistogramParams histogramParams, ILogger logger)
    {
        GrpcPreconditions.CheckArgument(outstandingRpcsPerChannel > 0, nameof(outstandingRpcsPerChannel));
        GrpcPreconditions.CheckNotNull(histogramParams, nameof(histogramParams));
        GrpcPreconditions.CheckNotNull(loadParams, nameof(loadParams));
        GrpcPreconditions.CheckNotNull(payloadConfig, nameof(payloadConfig));
        _channels = new List<GrpcChannel>(channels);
        _clientType = clientType;
        _rpcType = rpcType;
        _payloadConfig = payloadConfig;
        _logger = logger;
        _cachedByteBufferRequest = new Lazy<byte[]>(() => new byte[payloadConfig.BytebufParams.ReqSize]);
        _threadLocalHistogram = new ThreadLocal<Histogram>(() => new Histogram(histogramParams.Resolution, histogramParams.MaxPossible), true);

        _runnerTasks = new List<Task>();
        foreach (var channel in _channels)
        {
            for (var i = 0; i < outstandingRpcsPerChannel; i++)
            {
                var timer = CreateTimer(loadParams, 1.0 / _channels.Count / outstandingRpcsPerChannel);
                _runnerTasks.Add(RunClientAsync(channel, timer));
            }
        }
    }

    public ClientStats GetStats(bool reset)
    {
        var histogramData = new HistogramData();
        foreach (var hist in _threadLocalHistogram.Values)
        {
            hist.GetSnapshot(histogramData, reset);
        }

        var timeSnapshot = _timeStats.GetSnapshot(reset);

        if (reset)
        {
            _statsResetCount.Increment();
        }

        _logger.LogInformation(
            $"[ClientRunnerImpl.GetStats] GC collection counts: gen0 {GC.CollectionCount(0)}, gen1 {GC.CollectionCount(1)}, gen2 {GC.CollectionCount(2)}, (histogram reset count:{_statsResetCount.Count}, seconds since reset: {timeSnapshot.WallClockTime.TotalSeconds})");

        return new ClientStats
        {
            Latencies = histogramData,
            TimeElapsed = timeSnapshot.WallClockTime.TotalSeconds,
            TimeUser = timeSnapshot.UserProcessorTime.TotalSeconds,
            TimeSystem = timeSnapshot.PrivilegedProcessorTime.TotalSeconds
        };
    }

    public async Task StopAsync()
    {
        _stoppedCts.Cancel();
        foreach (var runnerTask in _runnerTasks)
        {
            await runnerTask;
        }
        foreach (var channel in _channels)
        {
            await channel.ShutdownAsync();
        }
    }

    private void RunUnary(GrpcChannel channel, IInterarrivalTimer timer)
    {
        var client = new BenchmarkService.BenchmarkServiceClient(channel);
        var request = CreateSimpleRequest();
        var stopwatch = new Stopwatch();

        while (!_stoppedCts.Token.IsCancellationRequested)
        {
            stopwatch.Restart();
            client.UnaryCall(request);
            stopwatch.Stop();

            // spec requires data point in nanoseconds.
            _threadLocalHistogram.Value!.AddObservation(stopwatch.Elapsed.TotalSeconds * SecondsToNanos);

            timer.WaitForNext();
        }
    }

    private async Task RunUnaryAsync(GrpcChannel channel, IInterarrivalTimer timer)
    {
        var client = new BenchmarkService.BenchmarkServiceClient(channel);
        var request = CreateSimpleRequest();
        var stopwatch = new Stopwatch();

        while (!_stoppedCts.Token.IsCancellationRequested)
        {
            stopwatch.Restart();
            await client.UnaryCallAsync(request);
            stopwatch.Stop();

            // spec requires data point in nanoseconds.
            _threadLocalHistogram.Value!.AddObservation(stopwatch.Elapsed.TotalSeconds * SecondsToNanos);

            await timer.WaitForNextAsync();
        }
    }

    private async Task RunStreamingPingPongAsync(GrpcChannel channel, IInterarrivalTimer timer)
    {
        var client = new BenchmarkService.BenchmarkServiceClient(channel);
        var request = CreateSimpleRequest();
        var stopwatch = new Stopwatch();

        using (var call = client.StreamingCall())
        {
            while (!_stoppedCts.Token.IsCancellationRequested)
            {
                stopwatch.Restart();
                await call.RequestStream.WriteAsync(request);
                await call.ResponseStream.MoveNext();
                stopwatch.Stop();

                // spec requires data point in nanoseconds.
                _threadLocalHistogram.Value!.AddObservation(stopwatch.Elapsed.TotalSeconds * SecondsToNanos);

                await timer.WaitForNextAsync();
            }

            // finish the streaming call
            await call.RequestStream.CompleteAsync();
            if (await call.ResponseStream.MoveNext())
            {
                throw new InvalidOperationException("Expected response stream end.");
            }
        }
    }

    private async Task RunGenericStreamingAsync(GrpcChannel channel, IInterarrivalTimer timer)
    {
        var request = _cachedByteBufferRequest.Value;
        var stopwatch = new Stopwatch();

        var invoker = channel.CreateCallInvoker();

        using (var call = invoker.AsyncDuplexStreamingCall(GenericService.StreamingCallMethod, host: null, new CallOptions()))
        {
            while (!_stoppedCts.Token.IsCancellationRequested)
            {
                stopwatch.Restart();
                await call.RequestStream.WriteAsync(request);
                await call.ResponseStream.MoveNext();
                stopwatch.Stop();

                // spec requires data point in nanoseconds.
                _threadLocalHistogram.Value!.AddObservation(stopwatch.Elapsed.TotalSeconds * SecondsToNanos);

                await timer.WaitForNextAsync();
            }

            // finish the streaming call
            await call.RequestStream.CompleteAsync();
            if (await call.ResponseStream.MoveNext())
            {
                throw new InvalidOperationException("Expected response stream end.");
            }
        }
    }

    private Task RunClientAsync(GrpcChannel channel, IInterarrivalTimer timer)
    {
        if (_payloadConfig.PayloadCase == PayloadConfig.PayloadOneofCase.BytebufParams)
        {
            GrpcPreconditions.CheckArgument(_clientType == ClientType.AsyncClient, "Generic client only supports async API");
            GrpcPreconditions.CheckArgument(_rpcType == RpcType.Streaming, "Generic client only supports streaming calls");
            return RunGenericStreamingAsync(channel, timer);
        }

        GrpcPreconditions.CheckNotNull(_payloadConfig.SimpleParams, "SimpleParams");
        if (_clientType == ClientType.SyncClient)
        {
            GrpcPreconditions.CheckArgument(_rpcType == RpcType.Unary, "Sync client can only be used for Unary calls in C#");
            // create a dedicated thread for the synchronous client
            return Task.Factory.StartNew(() => RunUnary(channel, timer), TaskCreationOptions.LongRunning);
        }
        else if (_clientType == ClientType.AsyncClient)
        {
            switch (_rpcType)
            {
                case RpcType.Unary:
                    return RunUnaryAsync(channel, timer);
                case RpcType.Streaming:
                    return RunStreamingPingPongAsync(channel, timer);
            }
        }
        throw new ArgumentException("Unsupported configuration.");
    }

    private SimpleRequest CreateSimpleRequest()
    {
        GrpcPreconditions.CheckNotNull(_payloadConfig.SimpleParams, "SimpleParams");
        return new SimpleRequest
        {
            Payload = CreateZerosPayload(_payloadConfig.SimpleParams.ReqSize),
            ResponseSize = _payloadConfig.SimpleParams.RespSize
        };
    }

    private static Payload CreateZerosPayload(int size)
    {
        return new Payload { Body = ByteString.CopyFrom(new byte[size]) };
    }

    private static IInterarrivalTimer CreateTimer(LoadParams loadParams, double loadMultiplier)
    {
        switch (loadParams.LoadCase)
        {
            case LoadParams.LoadOneofCase.ClosedLoop:
                return new ClosedLoopInterarrivalTimer();
            case LoadParams.LoadOneofCase.Poisson:
                return new PoissonInterarrivalTimer(loadParams.Poisson.OfferedLoad * loadMultiplier);
            default:
                throw new ArgumentException("Unknown load type");
        }
    }
}
