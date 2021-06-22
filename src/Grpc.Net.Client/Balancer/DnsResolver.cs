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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A <see cref="Resolver"/> that returns addresses queried from a DNS hostname.
    /// </summary>
    public sealed class DnsResolver : Resolver
    {
        private readonly Uri _address;
        private readonly TimeSpan _refreshInterval;
        private readonly ILogger<DnsResolver> _logger;
        private readonly object _lock = new object();

        private Timer? _timer;
        private Action<ResolverResult>? _listener;
        private bool _disposed;
        private Task _refreshTask;

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
        public override async Task RefreshAsync(CancellationToken cancellationToken)
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
                    _refreshTask = RefreshCoreAsync();
                }
            }

            await _refreshTask.ConfigureAwait(false);
        }

        private async Task RefreshCoreAsync()
        {
            CompatibilityHelpers.Assert(_listener != null);

            var dnsAddress = _address.AbsolutePath.TrimStart('/');
            _logger.LogTrace($"Getting DNS hosts from {dnsAddress}");

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(dnsAddress).ConfigureAwait(false);

                _logger.LogTrace($"{addresses.Length} DNS results from {dnsAddress}: " + string.Join<IPAddress>(", ", addresses));

                var resolvedPort = _address.Port == -1 ? 80 : _address.Port;
                var endpoints = addresses.Select(a => new DnsEndPoint(a.ToString(), resolvedPort)).ToArray();
                var resolverResult = ResolverResult.ForResult(endpoints, serviceConfig: null);
                _listener(resolverResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting DNS hosts from {dnsAddress}");
                _listener(ResolverResult.ForError(new Status(StatusCode.Unavailable, $"Error getting DNS hosts from {dnsAddress}", ex)));
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _timer?.Dispose();
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
                        _refreshTask = RefreshCoreAsync();
                        awaitRefresh = true;
                    }
                }

                if (awaitRefresh)
                {
                    await _refreshTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in timer");
            }
        }
    }

    /// <summary>
    /// A <see cref="ResolverFactory"/> that matches the URI scheme <c>dns</c>
    /// and creates <see cref="DnsResolver"/> instances.
    /// </summary>
    public sealed class DnsResolverFactory : ResolverFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly TimeSpan _refreshInterval;

        /// <summary>
        /// Initializes a new instance of the <see cref="DnsResolverFactory"/> class with a refresh interval.
        /// </summary>
        /// <param name="loggerFactory">A logger factory.</param>
        /// <param name="refreshInterval">An interval for automatically refreshing the DNS hostname.</param>
        public DnsResolverFactory(ILoggerFactory loggerFactory, TimeSpan refreshInterval)
        {
            _loggerFactory = loggerFactory;
            _refreshInterval = refreshInterval;
        }

        /// <inheritdoc />
        public override string Name => "dns";

        /// <inheritdoc />
        public override Resolver Create(Uri address, ResolverOptions options)
        {
            return new DnsResolver(address, _loggerFactory, _refreshInterval);
        }
    }
}
#endif
