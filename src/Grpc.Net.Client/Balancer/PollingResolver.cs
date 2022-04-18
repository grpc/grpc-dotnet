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

#if SUPPORT_LOAD_BALANCING
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// An abstract base type for <see cref="Resolver"/> implementations that use asynchronous polling logic to resolve the <see cref="Uri"/>.
    /// <para>
    /// <see cref="PollingResolver"/> adds a virtual <see cref="ResolveAsync"/> method. The resolver runs one asynchronous
    /// resolve task at a time. Calling <see cref="Refresh()"/> on the resolver when a resolve task is already running has
    /// no effect.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class PollingResolver : Resolver
    {
        // Internal for testing
        internal Task _resolveTask = Task.CompletedTask;
        private Action<ResolverResult>? _listener;
        private bool _disposed;
        private bool _resolveSuccessful;

        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ILogger _logger;
        private readonly IBackoffPolicyFactory? _backoffPolicyFactory;

        /// <summary>
        /// Gets the listener.
        /// </summary>
        protected Action<ResolverResult> Listener => _listener!;

        /// <summary>
        /// Initializes a new instance of the <see cref="PollingResolver"/>.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        protected PollingResolver(ILoggerFactory loggerFactory)
            : this(loggerFactory, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PollingResolver"/>.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="backoffPolicyFactory">The backoff policy factory.</param>
        protected PollingResolver(ILoggerFactory loggerFactory, IBackoffPolicyFactory? backoffPolicyFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<PollingResolver>();
            _backoffPolicyFactory = backoffPolicyFactory;
        }

        /// <summary>
        /// Starts listening to resolver for results with the specified callback. Can only be called once.
        /// <para>
        /// The <see cref="ResolverResult"/> passed to the callback has addresses when successful,
        /// otherwise a <see cref="Status"/> details the resolution error.
        /// </para>
        /// </summary>
        /// <param name="listener">The callback used to receive updates on the target.</param>
        public override sealed void Start(Action<ResolverResult> listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (_listener != null)
            {
                throw new InvalidOperationException("Resolver has already been started.");
            }

            _listener = (result) =>
            {
                if (result.Status.StatusCode == StatusCode.OK)
                {
                    _resolveSuccessful = true;
                }
                Log.ResolveResult(_logger, GetType(), result.Status.StatusCode, result.Addresses?.Count ?? 0);
                listener(result);
            };

            OnStarted();
        }

        /// <summary>
        /// Executes after the resolver starts.
        /// </summary>
        protected virtual void OnStarted()
        {
        }

        /// <summary>
        /// Refresh resolution. Can only be called after <see cref="Start(Action{ResolverResult})"/>.
        /// <para>
        /// The resolver runs one asynchronous resolve task at a time. Calling <see cref="Refresh()"/> on the resolver when a
        /// resolve task is already running has no effect.
        /// </para>
        /// </summary>
        public sealed override void Refresh()
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
                Log.ResolverRefreshRequested(_logger, GetType());

                if (_resolveTask.IsCompleted)
                {
                    _resolveTask = ResolveNowAsync(_cts.Token);
                }
                else
                {
                    Log.ResolverRefreshIgnored(_logger, GetType());
                }
            }
        }

        private async Task ResolveNowAsync(CancellationToken cancellationToken)
        {
            // Reset resolve success to false. Will be set to true when an OK result is sent to listener.
            _resolveSuccessful = false;

            try
            {
                var backoffPolicy = _backoffPolicyFactory?.Create();

                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        await ResolveAsync(cancellationToken).ConfigureAwait(false);

                        // ResolveAsync may report a failure but not throw. Check to see whether an OK result
                        // has been reported. If not then start retry loop.
                        if (_resolveSuccessful)
                        {
                            return;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Ignore cancellation.
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.ResolveError(_logger, GetType(), ex);

                        var status = GrpcProtocolHelpers.CreateStatusFromException("Error refreshing resolver.", ex);
                        Listener(ResolverResult.ForFailure(status));
                    }

                    // No backoff policy specified. Exit immediately.
                    if (backoffPolicy == null)
                    {
                        break;
                    }

                    var backoffTicks = backoffPolicy.NextBackoff().Ticks;
                    // Task.Delay supports up to Int32.MaxValue milliseconds.
                    // Note that even if the maximum backoff is configured to this maximum, the jitter could push it over the limit.
                    // Force an upper bound here to ensure an unsupported backoff is never used.
                    backoffTicks = Math.Min(backoffTicks, TimeSpan.TicksPerMillisecond * int.MaxValue);

                    var backkoff = TimeSpan.FromTicks(backoffTicks);
                    Log.StartingResolveBackoff(_logger, GetType(), backkoff);
                    await Task.Delay(backkoff, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore cancellation.
            }
            catch (Exception ex)
            {
                Log.ErrorRetryingResolve(_logger, GetType(), ex);
            }
        }

        /// <summary>
        /// Resolve the target <see cref="Uri"/>. Updated results are passed to the callback
        /// registered by <see cref="Start(Action{ResolverResult})"/>. Can only be called
        /// after the resolver has started.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task.</returns>
        protected abstract Task ResolveAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="LoadBalancer"/> and optionally releases
        /// the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            _cts.Cancel();
            _disposed = true;
        }

        internal static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _resolverRefreshRequested =
                LoggerMessage.Define<string>(LogLevel.Trace, new EventId(1, "ResolverRefreshRequested"), "{ResolveType} refresh requested.");

            private static readonly Action<ILogger, string, Exception?> _resolverRefreshIgnored =
                LoggerMessage.Define<string>(LogLevel.Trace, new EventId(2, "ResolverRefreshIgnored"), "{ResolveType} refresh ignored because resolve is already in progress.");

            private static readonly Action<ILogger, string, Exception> _resolveError =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "ResolveError"), "Error resolving {ResolveType}.");

            private static readonly Action<ILogger, string, StatusCode, int, Exception?> _resolveResult =
                LoggerMessage.Define<string, StatusCode, int>(LogLevel.Trace, new EventId(4, "ResolveResult"), "{ResolveType} result with status code '{StatusCode}' and {AddressCount} addresses.");

            private static readonly Action<ILogger, string, TimeSpan, Exception?> _startingResolveBackoff =
                LoggerMessage.Define<string, TimeSpan>(LogLevel.Trace, new EventId(5, "StartingResolveBackoff"), "{ResolveType} starting resolve backoff of {BackoffDuration}.");

            private static readonly Action<ILogger, string, Exception> _errorRetryingResolve =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, "ErrorRetryingResolve"), "{ResolveType} error retrying resolve.");

            public static void ResolverRefreshRequested(ILogger logger, Type resolverType)
            {
                _resolverRefreshRequested(logger, resolverType.Name, null);
            }

            public static void ResolverRefreshIgnored(ILogger logger, Type resolverType)
            {
                _resolverRefreshIgnored(logger, resolverType.Name, null);
            }

            public static void ResolveError(ILogger logger, Type resolverType, Exception ex)
            {
                _resolveError(logger, resolverType.Name, ex);
            }

            public static void ResolveResult(ILogger logger, Type resolverType, StatusCode statusCode, int addressCount)
            {
                _resolveResult(logger, resolverType.Name, statusCode, addressCount, null);
            }

            public static void StartingResolveBackoff(ILogger logger, Type resolverType, TimeSpan delay)
            {
                _startingResolveBackoff(logger, resolverType.Name, delay, null);
            }

            public static void ErrorRetryingResolve(ILogger logger, Type resolverType, Exception ex)
            {
                _errorRetryingResolve(logger, resolverType.Name, ex);
            }
        }
    }
}
#endif
