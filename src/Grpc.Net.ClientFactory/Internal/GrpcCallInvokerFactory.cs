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

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace Grpc.Net.ClientFactory.Internal
{
    internal record struct EntryKey(string Name, Type Type);

    internal class GrpcCallInvokerFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsMonitor<GrpcClientFactoryOptions> _grpcClientFactoryOptionsMonitor;
        private readonly IOptionsMonitor<HttpClientFactoryOptions> _httpClientFactoryOptionsMonitor;
        private readonly IHttpMessageHandlerFactory _messageHandlerFactory;

        private static readonly TimerCallback _cleanupCallback = (s) => ((GrpcCallInvokerFactory)s!).CleanupTimer_Tick();
        private readonly ILogger _logger;
        private readonly IServiceProvider _services;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Func<EntryKey, Lazy<ActiveChannelTrackingEntry>> _entryFactory;

        // Default time of 10s for cleanup seems reasonable.
        // Quick math:
        // 10 distinct named clients * expiry time >= 1s = approximate cleanup queue of 100 items
        //
        // This seems frequent enough. We also rely on GC occurring to actually trigger disposal.
        private readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromSeconds(10);

        // We use a new timer for each regular cleanup cycle, protected with a lock. Note that this scheme
        // doesn't give us anything to dispose, as the timer is started/stopped as needed.
        //
        // There's no need for the factory itself to be disposable. If you stop using it, eventually everything will
        // get reclaimed.
        private Timer? _cleanupTimer;
        private readonly object _cleanupTimerLock;
        private readonly object _cleanupActiveLock;

        // Collection of 'active' channels.
        //
        // Using lazy for synchronization to ensure that only one channel instance is created
        // for each name.
        //
        // internal for tests
        internal readonly ConcurrentDictionary<EntryKey, Lazy<ActiveChannelTrackingEntry>> _activeChannels;

        // Collection of 'expired' but not yet disposed channels.
        //
        // Used when we're rotating channels so that we can dispose channel instances once they
        // are eligible for garbage collection.
        //
        // internal for tests
        internal readonly ConcurrentQueue<ExpiredChannelTrackingEntry> _expiredChannels;
        private readonly TimerCallback _expiryCallback;

        public GrpcCallInvokerFactory(
            IServiceProvider services,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            IOptionsMonitor<GrpcClientFactoryOptions> grpcClientFactoryOptionsMonitor,
            IOptionsMonitor<HttpClientFactoryOptions> httpClientFactoryOptionsMonitor,
            IHttpMessageHandlerFactory messageHandlerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _loggerFactory = loggerFactory;
            _grpcClientFactoryOptionsMonitor = grpcClientFactoryOptionsMonitor;
            _httpClientFactoryOptionsMonitor = httpClientFactoryOptionsMonitor;
            _messageHandlerFactory = messageHandlerFactory;

            _services = services;
            _scopeFactory = scopeFactory;

            _logger = loggerFactory.CreateLogger<GrpcCallInvokerFactory>();

            // case-sensitive because named options is.
            _activeChannels = new ConcurrentDictionary<EntryKey, Lazy<ActiveChannelTrackingEntry>>();
            _entryFactory = (name) =>
            {
                return new Lazy<ActiveChannelTrackingEntry>(() =>
                {
                    return CreateChannelEntry(name);
                }, LazyThreadSafetyMode.ExecutionAndPublication);
            };

            _expiredChannels = new ConcurrentQueue<ExpiredChannelTrackingEntry>();
            _expiryCallback = ExpiryTimer_Tick;

            _cleanupTimerLock = new object();
            _cleanupActiveLock = new object();
        }

        public CallInvoker CreateInvoker(string name, Type type)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            ActiveChannelTrackingEntry entry = _activeChannels.GetOrAdd(new EntryKey(name, type), _entryFactory).Value;

            StartHandlerEntryTimer(entry);

            return entry.CallInvoker;
        }

        // Internal for tests
        internal ActiveChannelTrackingEntry CreateChannelEntry(EntryKey key)
        {
            var (name, type) = (key.Name, key.Type);

            var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var httpClientFactoryOptions = _httpClientFactoryOptionsMonitor.Get(name);
                if (httpClientFactoryOptions.HttpClientActions.Count > 0)
                {
                    throw new InvalidOperationException($"The ConfigureHttpClient method is not supported when creating gRPC clients. Unable to create client with name '{name}'.");
                }

                var clientFactoryOptions = _grpcClientFactoryOptionsMonitor.Get(name);
                var httpHandler = _messageHandlerFactory.CreateHandler(name); if (httpHandler == null)
                {
                    throw new ArgumentNullException(nameof(httpHandler));
                }

                var channelOptions = new GrpcChannelOptions();
                channelOptions.HttpHandler = httpHandler;
                channelOptions.LoggerFactory = _loggerFactory;
                channelOptions.ServiceProvider = services;

                if (clientFactoryOptions.ChannelOptionsActions.Count > 0)
                {
                    foreach (var applyOptions in clientFactoryOptions.ChannelOptionsActions)
                    {
                        applyOptions(channelOptions);
                    }
                }

                var address = clientFactoryOptions.Address;
                if (address == null)
                {
                    throw new InvalidOperationException($@"Could not resolve the address for gRPC client '{name}'. Set an address when registering the client: services.AddGrpcClient<{type.Name}>(o => o.Address = new Uri(""https://localhost:5001""))");
                }

                var channel = GrpcChannel.ForAddress(address, channelOptions);

                var httpClientCallInvoker = channel.CreateCallInvoker();

                var resolvedCallInvoker = GrpcClientFactoryOptions.BuildInterceptors(
                    httpClientCallInvoker,
                    services,
                    clientFactoryOptions,
                    InterceptorLifetime.Channel);

                // Wrap the invoker so we can ensure the inner invoker outlives the outer invoker.
                var handler = new LifetimeTrackingCallInvoker(resolvedCallInvoker, channel);

                // Note that we can't start the timer here. That would introduce a very very subtle race condition
                // with very short expiry times. We need to wait until we've actually handed out the channel once
                // to start the timer.
                //
                // Otherwise it would be possible that we start the timer here, immediately expire it (very short
                // timer) and then dispose it without ever creating a client. That would be bad. It's unlikely
                // this would happen, but we want to be sure.
                return new ActiveChannelTrackingEntry(key, handler, scope, Timeout.InfiniteTimeSpan);
            }
            catch
            {
                // If something fails while creating the handler, dispose the services.
                scope?.Dispose();
                throw;
            }
        }

        // Internal for tests
        internal void ExpiryTimer_Tick(object? state)
        {
            var active = (ActiveChannelTrackingEntry)state!;

            // The timer callback should be the only one removing from the active collection. If we can't find
            // our entry in the collection, then this is a bug.
            var removed = _activeChannels.TryRemove(active.Key, out var found);
            Debug.Assert(removed, "Entry not found. We should always be able to remove the entry");
            Debug.Assert(object.ReferenceEquals(active, found!.Value), "Different entry found. The entry should not have been replaced");

            // At this point the channel is no longer 'active' and will not be handed out to any new clients.
            // However we haven't dropped our strong reference to the channel, so we can't yet determine if
            // there are still any other outstanding references (we know there is at least one).
            //
            // We use a different state object to track expired channels. This allows any other thread that acquired
            // the 'active' entry to use it without safety problems.
            var expired = new ExpiredChannelTrackingEntry(active);
            _expiredChannels.Enqueue(expired);

            Log.ChannelExpired(_logger, active.Key.Name, active.Lifetime);

            StartCleanupTimer();
        }

        // Internal so it can be overridden in tests
        internal virtual void StartHandlerEntryTimer(ActiveChannelTrackingEntry entry)
        {
            entry.StartExpiryTimer(_expiryCallback);
        }

        // Internal so it can be overridden in tests
        internal virtual void StartCleanupTimer()
        {
            lock (_cleanupTimerLock)
            {
                if (_cleanupTimer == null)
                {
                    _cleanupTimer = NonCapturingTimer.Create(_cleanupCallback, this, DefaultCleanupInterval, Timeout.InfiniteTimeSpan);
                }
            }
        }

        // Internal so it can be overridden in tests
        internal virtual void StopCleanupTimer()
        {
            lock (_cleanupTimerLock)
            {
                _cleanupTimer!.Dispose();
                _cleanupTimer = null;
            }
        }

        // Internal for tests
        internal void CleanupTimer_Tick()
        {
            // Stop any pending timers, we'll restart the timer if there's anything left to process after cleanup.
            //
            // With the scheme we're using it's possible we could end up with some redundant cleanup operations.
            // This is expected and fine.
            //
            // An alternative would be to take a lock during the whole cleanup process. This isn't ideal because it
            // would result in threads executing ExpiryTimer_Tick as they would need to block on cleanup to figure out
            // whether we need to start the timer.
            StopCleanupTimer();

            if (!Monitor.TryEnter(_cleanupActiveLock))
            {
                // We don't want to run a concurrent cleanup cycle. This can happen if the cleanup cycle takes
                // a long time for some reason. Since we're running user code inside Dispose, it's definitely
                // possible.
                //
                // If we end up in that position, just make sure the timer gets started again. It should be cheap
                // to run a 'no-op' cleanup.
                StartCleanupTimer();
                return;
            }

            try
            {
                int initialCount = _expiredChannels.Count;
                Log.CleanupCycleStart(_logger, initialCount);

                var startTimestamp = Stopwatch.GetTimestamp();

                int disposedCount = 0;
                for (int i = 0; i < initialCount; i++)
                {
                    // Since we're the only one removing from _expired, TryDequeue must always succeed.
                    _expiredChannels.TryDequeue(out var entry);
                    CompatibilityHelpers.Assert(entry != null, "Entry was null, we should always get an entry back from TryDequeue");

                    if (entry.CanDispose)
                    {
                        try
                        {
                            entry.Channel.Dispose();
                            entry.Scope?.Dispose();
                            disposedCount++;
                        }
                        catch (Exception ex)
                        {
                            Log.CleanupItemFailed(_logger, entry.Key.Name, ex);
                        }
                    }
                    else
                    {
                        // If the entry is still live, put it back in the queue so we can process it
                        // during the next cleanup cycle.
                        _expiredChannels.Enqueue(entry);
                    }
                }

                var timestampDelta = startTimestamp - Stopwatch.GetTimestamp();
                var ticks = (long)((TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency) * timestampDelta);
                var elapsed = TimeSpan.FromTicks(ticks);

                Log.CleanupCycleEnd(_logger, elapsed, disposedCount, _expiredChannels.Count);
            }
            finally
            {
                Monitor.Exit(_cleanupActiveLock);
            }

            // We didn't totally empty the cleanup queue, try again later.
            if (!_expiredChannels.IsEmpty)
            {
                StartCleanupTimer();
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, int, Exception?> _cleanupCycleStart = LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(1, "CleanupCycleStart"),
                "Starting GrpcChannel cleanup cycle with {InitialCount} items");

            private static readonly Action<ILogger, double, int, int, Exception?> _cleanupCycleEnd = LoggerMessage.Define<double, int, int>(
                LogLevel.Debug,
                new EventId(2, "CleanupCycleEnd"),
                "Ending GrpcChannel cleanup cycle after {ElapsedMilliseconds}ms - processed: {DisposedCount} items - remaining: {RemainingItems} items");

            private static readonly Action<ILogger, string, Exception> _cleanupItemFailed = LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(3, "CleanupItemFailed"),
                "GrpcChannel.Dispose() threw an unhandled exception for client: '{ClientName}'");

            private static readonly Action<ILogger, double, string, Exception?> _channelExpired = LoggerMessage.Define<double, string>(
                LogLevel.Debug,
                new EventId(4, "ChannelExpired"),
                "GrpcChannel expired after {HandlerLifetime}ms for client '{ClientName}'");

            public static void CleanupCycleStart(ILogger logger, int initialCount)
            {
                _cleanupCycleStart(logger, initialCount, null);
            }

            public static void CleanupCycleEnd(ILogger logger, TimeSpan duration, int disposedCount, int finalCount)
            {
                _cleanupCycleEnd(logger, duration.TotalMilliseconds, disposedCount, finalCount, null);
            }

            public static void CleanupItemFailed(ILogger logger, string clientName, Exception exception)
            {
                _cleanupItemFailed(logger, clientName, exception);
            }

            public static void ChannelExpired(ILogger logger, string clientName, TimeSpan lifetime)
            {
                _channelExpired(logger, lifetime.TotalMilliseconds, clientName, null);
            }
        }
    }
}
