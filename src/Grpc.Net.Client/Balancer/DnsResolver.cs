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

#if SUPPORT_LOAD_BALANCING
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Internal;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer;

internal sealed class DnsResolver : PollingResolver
{
    // To prevent excessive re-resolution, we enforce a rate limit on DNS resolution requests.
    private static readonly TimeSpan MinimumDnsResolutionRate = TimeSpan.FromSeconds(15);

    private readonly Uri _originalAddress;
    private readonly string _dnsAddress;
    private readonly int _port;
    private readonly TimeSpan _refreshInterval;
    private readonly ILogger _logger;

    private Timer? _timer;
    private DateTime _lastResolveStart;

    // Internal for testing.
    internal ISystemClock SystemClock = Client.Internal.SystemClock.Instance;

    public DnsResolver(Uri address, int defaultPort, ILoggerFactory loggerFactory, TimeSpan refreshInterval, IBackoffPolicyFactory backoffPolicyFactory) : base(loggerFactory, backoffPolicyFactory)
    {
        _originalAddress = address;

        // DNS address has the format: dns:[//authority/]host[:port]
        // Because the host is specified in the path, the port needs to be parsed manually
        var addressParsed = new Uri("temp://" + address.AbsolutePath.TrimStart('/'));
        _dnsAddress = addressParsed.Host;
        _port = addressParsed.Port == -1 ? defaultPort : addressParsed.Port;
        _refreshInterval = refreshInterval;
        _logger = loggerFactory.CreateLogger(typeof(DnsResolver));
    }

    protected override void OnStarted()
    {
        base.OnStarted();

        if (_refreshInterval != Timeout.InfiniteTimeSpan)
        {
            _timer = NonCapturingTimer.Create(OnTimerCallback, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(_refreshInterval, _refreshInterval);
        }
    }

    protected override async Task ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var elapsedTimeSinceLastRefresh = SystemClock.UtcNow - _lastResolveStart;
            if (elapsedTimeSinceLastRefresh < MinimumDnsResolutionRate)
            {
                var delay = MinimumDnsResolutionRate - elapsedTimeSinceLastRefresh;
                DnsResolverLog.StartingRateLimitDelay(_logger, delay, MinimumDnsResolutionRate);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var lastResolveStart = SystemClock.UtcNow;

            if (string.IsNullOrEmpty(_dnsAddress))
            {
                throw new InvalidOperationException($"Resolver address '{_originalAddress}' is not valid. Please use dns:/// for DNS provider.");
            }

            DnsResolverLog.StartingDnsQuery(_logger, _dnsAddress);
            var addresses =
#if NET6_0_OR_GREATER                    
                await Dns.GetHostAddressesAsync(_dnsAddress, cancellationToken).ConfigureAwait(false);
#else
                await Dns.GetHostAddressesAsync(_dnsAddress).ConfigureAwait(false);
#endif

            DnsResolverLog.ReceivedDnsResults(_logger, addresses.Length, _dnsAddress, addresses);

            var hostOverride = $"{_dnsAddress}:{_port}";
            var endpoints = addresses.Select(a =>
            {
                var address = new BalancerAddress(a.ToString(), _port);
                address.Attributes.Set(ConnectionManager.HostOverrideKey, hostOverride);
                return address;
            }).ToArray();
            var resolverResult = ResolverResult.ForResult(endpoints);
            Listener(resolverResult);

            // Only update last resolve start if successful. Backoff will handle limiting resolves on failure.
            _lastResolveStart = lastResolveStart;
        }
        catch (Exception ex)
        {
            var message = $"Error getting DNS hosts for address '{_dnsAddress}'.";

            DnsResolverLog.ErrorQueryingDns(_logger, _dnsAddress, ex);
            Listener(ResolverResult.ForFailure(GrpcProtocolHelpers.CreateStatusFromException(message, ex, StatusCode.Unavailable)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _timer?.Dispose();
    }

    private void OnTimerCallback(object? state)
    {
        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            DnsResolverLog.ErrorFromRefreshInterval(_logger, ex);
        }
    }
}

internal static partial class DnsResolverLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "StartingRateLimitDelay", Message = "Starting rate limit delay of {DelayDuration}. DNS resolution rate limit is once every {RateLimitDuration}.")]
    public static partial void StartingRateLimitDelay(ILogger logger, TimeSpan delayDuration, TimeSpan rateLimitDuration);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 2, EventName = "StartingDnsQuery", Message = "Starting DNS query to get hosts from '{DnsAddress}'.")]
    public static partial void StartingDnsQuery(ILogger logger, string dnsAddress);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 3, EventName = "ReceivedDnsResults", Message = "Received {ResultCount} DNS results from '{DnsAddress}'. Results: {DnsResults}")]
    private static partial void ReceivedDnsResults(ILogger logger, int resultCount, string dnsAddress, string dnsResults);

    public static void ReceivedDnsResults(ILogger logger, int resultCount, string dnsAddress, IList<IPAddress> dnsResults)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            ReceivedDnsResults(logger, resultCount, dnsAddress, string.Join(", ", dnsResults));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, EventId = 4, EventName = "ErrorQueryingDns", Message = "Error querying DNS hosts for '{DnsAddress}'.")]
    public static partial void ErrorQueryingDns(ILogger logger, string dnsAddress, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, EventId = 5, EventName = "ErrorFromRefreshIntervalTimer", Message = "Error from refresh interval timer.")]
    public static partial void ErrorFromRefreshInterval(ILogger logger, Exception ex);
}

/// <summary>
/// A <see cref="ResolverFactory"/> that matches the URI scheme <c>dns</c>
/// and creates <see cref="DnsResolver"/> instances.
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
public sealed class DnsResolverFactory : ResolverFactory
{
    private readonly TimeSpan _refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsResolverFactory"/> class with a refresh interval.
    /// </summary>
    /// <param name="refreshInterval">An interval for automatically refreshing the DNS hostname.</param>
    public DnsResolverFactory(TimeSpan refreshInterval)
    {
        _refreshInterval = refreshInterval;
    }

    /// <inheritdoc />
    public override string Name => "dns";

    /// <inheritdoc />
    public override Resolver Create(ResolverOptions options)
    {
        var channelOptions = options.ChannelOptions;

        var randomGenerator = channelOptions.ResolveService<IRandomGenerator>(
            new RandomGenerator());
        var backoffPolicyFactory = channelOptions.ResolveService<IBackoffPolicyFactory>(
            new ExponentialBackoffPolicyFactory(randomGenerator, channelOptions.InitialReconnectBackoff, channelOptions.MaxReconnectBackoff));

        return new DnsResolver(options.Address, options.DefaultPort, options.LoggerFactory, _refreshInterval, backoffPolicyFactory);
    }
}
#endif
