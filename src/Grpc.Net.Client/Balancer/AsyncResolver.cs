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
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// An abstract base type for <see cref="Resolver"/> implementations that use asynchronous logic to resolve the <see cref="Uri"/>.
    /// <para>
    /// <see cref="AsyncResolver"/> adds a virtual <see cref="ResolveAsync"/> method. The resolver runs one asynchronous
    /// resolve task at a time. Calling <see cref="Refresh()"/> on the resolver when a resolve task is already running has
    /// no effect.
    /// </para>
    /// </summary>
    public abstract class AsyncResolver : Resolver
    {
        // Internal for testing
        internal Task _resolveTask = Task.CompletedTask;
        private Action<ResolverResult>? _listener;
        private bool _disposed;

        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ILogger _logger;

        /// <summary>
        /// Gets the listener.
        /// </summary>
        protected Action<ResolverResult> Listener => _listener!;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncResolver"/>.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        protected AsyncResolver(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<AsyncResolver>();
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
                Log.ResolverResult(_logger, GetType(), result.Status.StatusCode, result.Addresses?.Count ?? 0);
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
        /// This is only a hint. Implementation takes it as a signal but may not start resolution.
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
            try
            {
                await ResolveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore cancellation.
            }
            catch (Exception ex)
            {
                Log.ResolverRefreshError(_logger, GetType(), ex);

                var status = GrpcProtocolHelpers.CreateStatusFromException("Error refreshing resolver.", ex);
                Listener(ResolverResult.ForFailure(status));
            }
        }

        /// <summary>
        /// Resolve the target <see cref="Uri"/>. Updated results are passed to the callback
        /// registered by <see cref="Start(Action{ResolverResult})"/>. Can only be called
        /// after the resolver has started.
        /// <para>
        /// This is only a hint. Implementation takes it as a signal but may not start resolution.
        /// </para>
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

            private static readonly Action<ILogger, string, Exception?> _resolverRefreshError =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "ResolverRefreshError"), "Error refreshing {ResolveType}.");

            private static readonly Action<ILogger, string, StatusCode, int, Exception?> _resolverResult =
                LoggerMessage.Define<string, StatusCode, int>(LogLevel.Trace, new EventId(4, "ResolverResult"), "{ResolveType} result with status code '{StatusCode}' and {AddressCount} addresses.");

            public static void ResolverRefreshRequested(ILogger logger, Type resolverType)
            {
                _resolverRefreshRequested(logger, resolverType.Name, null);
            }

            public static void ResolverRefreshIgnored(ILogger logger, Type resolverType)
            {
                _resolverRefreshIgnored(logger, resolverType.Name, null);
            }

            public static void ResolverRefreshError(ILogger logger, Type resolverType, Exception ex)
            {
                _resolverRefreshError(logger, resolverType.Name, ex);
            }

            public static void ResolverResult(ILogger logger, Type resolverType, StatusCode statusCode, int addressCount)
            {
                _resolverResult(logger, resolverType.Name, statusCode, addressCount, null);
            }
        }
    }
}
#endif
