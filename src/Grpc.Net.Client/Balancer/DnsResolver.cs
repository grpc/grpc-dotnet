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
using Grpc.Net.Client.Internal;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A <see cref="Resolver"/> that returns addresses queried from a DNS hostname.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    internal sealed class DnsResolver : Resolver
    {
        // To prevent excessive re-resolution, we enforce a rate limit on DNS resolution requests.
        private static readonly TimeSpan MinimumDnsResolutionRate = TimeSpan.FromSeconds(15);

        private readonly Uri _address;
        private readonly TimeSpan _refreshInterval;
        private readonly ILogger<DnsResolver> _logger;
        private readonly CancellationTokenSource _cts;
        private readonly object _lock = new object();

        private Timer? _timer;
        private Action<ResolverResult>? _listener;
        private bool _disposed;
        private Task _refreshTask;
        private DateTime _lastResolveStart;

        // Internal for testing.
        internal ISystemClock SystemClock = Client.Internal.SystemClock.Instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="DnsResolver"/> class with the specified target <see cref="Uri"/>.
        /// </summary>
        /// <param name="address">The target <see cref="Uri"/>.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="refreshInterval">An interval for automatically refreshing the DNS hostname.</param>
        public DnsResolver(Uri address, ILoggerFactory loggerFactory, TimeSpan refreshInterval)
        {
            _address = address;
            _refreshInterval = refreshInterval;
            _cts = new CancellationTokenSource();
            _logger = loggerFactory.CreateLogger<DnsResolver>();
            _refreshTask = Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Start(Action<ResolverResult> listener)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DnsResolver));
            }
            if (_listener != null)
            {
                throw new InvalidOperationException("Resolver has already been started.");
            }

            _listener = listener;

            if (_refreshInterval != Timeout.InfiniteTimeSpan)
            {
                _timer = new Timer(OnTimerCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _timer.Change(_refreshInterval, _refreshInterval);
            }
        }

        /// <inheritdoc />
        public override Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DnsResolver));
            }
            if (_listener == null)
            {
                throw new InvalidOperationException("Resolver hasn't been started.");
            }

            lock (_lock)
            {
                if (_refreshTask.IsCompleted)
                {
                    _refreshTask = RefreshCoreAsync(cancellationToken);
                }
            }

            return _refreshTask;
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            CompatibilityHelpers.Assert(_listener != null);

            try
            {
                var elapsedTimeSinceLastRefresh = SystemClock.UtcNow - _lastResolveStart;
                if (elapsedTimeSinceLastRefresh < MinimumDnsResolutionRate)
                {
                    var delay = MinimumDnsResolutionRate - elapsedTimeSinceLastRefresh;
                    DnsResolverLog.StartingRateLimitDelay(_logger, delay, MinimumDnsResolutionRate);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                _lastResolveStart = SystemClock.UtcNow;

                var dnsAddress = _address.AbsolutePath.TrimStart('/');

                if (string.IsNullOrEmpty(dnsAddress))
                {
                    throw new InvalidOperationException($"Resolver address '{_address}' doesn't have a path.");
                }

                DnsResolverLog.StartingDnsQuery(_logger, _address);
                var addresses = await Dns.GetHostAddressesAsync(dnsAddress).ConfigureAwait(false);

                DnsResolverLog.ReceivedDnsResults(_logger, addresses.Length, _address, addresses);

                var resolvedPort = _address.Port == -1 ? 80 : _address.Port;
                var endpoints = addresses.Select(a => new BalancerAddress(a.ToString(), resolvedPort)).ToArray();
                var resolverResult = ResolverResult.ForResult(endpoints);
                _listener(resolverResult);
            }
            catch (Exception ex)
            {
                var message = $"Error getting DNS hosts for address '{_address}'.";

                DnsResolverLog.ErrorQueryingDns(_logger, _address, ex);
                _listener(ResolverResult.ForFailure(GrpcProtocolHelpers.CreateStatusFromException(message, ex, StatusCode.Unavailable)));
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _timer?.Dispose();
            _cts.Cancel();
            _listener = null;
            _disposed = true;
        }

        private async void OnTimerCallback(object? state)
        {
            try
            {
                var awaitRefresh = false;
                lock (_lock)
                {
                    if (_refreshTask.IsCompleted)
                    {
                        _refreshTask = RefreshCoreAsync(_cts.Token);
                        awaitRefresh = true;
                    }
                }

                if (awaitRefresh)
                {
                    await _refreshTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Don't log cancellation.
            }
            catch (Exception ex)
            {
                DnsResolverLog.ErrorFromRefreshInterval(_logger, ex);
            }
        }
    }

    internal static class DnsResolverLog
    {
        private static readonly Action<ILogger, TimeSpan, TimeSpan, Exception?> _startingRateLimitDelay =
            LoggerMessage.Define<TimeSpan, TimeSpan>(LogLevel.Debug, new EventId(1, "StartingRateLimitDelay"), "Starting rate limit delay of {DelayDuration}. DNS resolution rate limit is once every {RateLimitDuration}.");

        private static readonly Action<ILogger, Uri, Exception?> _startingDnsQuery =
            LoggerMessage.Define<Uri>(LogLevel.Trace, new EventId(2, "StartingDnsQuery"), "Starting DNS query to get hosts from '{DnsAddress}'.");

        private static readonly Action<ILogger, int, Uri, string, Exception?> _receivedDnsResults =
            LoggerMessage.Define<int, Uri, string>(LogLevel.Debug, new EventId(3, "ReceivedDnsResults"), "Received {ResultCount} DNS results from '{DnsAddress}'. Results: {DnsResults}");

        private static readonly Action<ILogger, Uri, Exception?> _errorQueryingDns =
            LoggerMessage.Define<Uri>(LogLevel.Error, new EventId(4, "ErrorQueryingDns"), "Error querying DNS hosts for '{DnsAddress}'.");

        private static readonly Action<ILogger, Exception?> _errorFromRefreshInterval =
            LoggerMessage.Define(LogLevel.Error, new EventId(5, "ErrorFromRefreshIntervalTimer"), "Error from refresh interval timer.");

        public static void StartingRateLimitDelay(ILogger logger, TimeSpan delayDuration, TimeSpan rateLimitDuration)
        {
            _startingRateLimitDelay(logger, delayDuration, rateLimitDuration, null);
        }

        public static void StartingDnsQuery(ILogger logger, Uri dnsAddress)
        {
            _startingDnsQuery(logger, dnsAddress, null);
        }

        public static void ReceivedDnsResults(ILogger logger, int resultCount, Uri dnsAddress, IList<IPAddress> dnsResults)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _receivedDnsResults(logger, resultCount, dnsAddress, string.Join(", ", dnsResults), null);
            }
        }

        public static void ErrorQueryingDns(ILogger logger, Uri dnsAddress, Exception ex)
        {
            _errorQueryingDns(logger, dnsAddress, ex);
        }

        public static void ErrorFromRefreshInterval(ILogger logger, Exception ex)
        {
            _errorFromRefreshInterval(logger, ex);
        }
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
            return new DnsResolver(options.Address, options.LoggerFactory, _refreshInterval);
        }
    }
}
#endif
