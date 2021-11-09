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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal
{
    internal class ConnectionManager : IDisposable, IChannelControlHelper
    {
        private static readonly ServiceConfig DefaultServiceConfig = new ServiceConfig();

        private readonly SemaphoreSlim _nextPickerLock;
        private readonly object _lock;
        internal readonly Resolver _resolver;
        private readonly ISubchannelTransportFactory _subchannelTransportFactory;
        private readonly List<Subchannel> _subchannels;
        private readonly List<StateWatcher> _stateWatchers;
        private readonly CancellationTokenSource _cts;

        // Internal for testing
        internal LoadBalancer? _balancer;
        internal SubchannelPicker? _picker;
        private Task? _resolverRefreshTask;
        private Task? _resolveTask;
        private TaskCompletionSource<SubchannelPicker> _nextPickerTcs;
        private int _currentSubchannelId;
        private ServiceConfig? _previousServiceConfig;

        internal ConnectionManager(
            Resolver resolver,
            bool disableResolverServiceConfig,
            ILoggerFactory loggerFactory,
            ISubchannelTransportFactory subchannelTransportFactory,
            LoadBalancerFactory[] loadBalancerFactories)
        {
            _lock = new object();
            _nextPickerLock = new SemaphoreSlim(1);
            _nextPickerTcs = new TaskCompletionSource<SubchannelPicker>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cts = new CancellationTokenSource();

            Logger = loggerFactory.CreateLogger(GetType());
            LoggerFactory = loggerFactory;

            _subchannels = new List<Subchannel>();
            _stateWatchers = new List<StateWatcher>();
            _resolver = resolver;
            DisableResolverServiceConfig = disableResolverServiceConfig;
            _subchannelTransportFactory = subchannelTransportFactory;
            LoadBalancerFactories = loadBalancerFactories;
        }

        public ConnectivityState State { get; private set; }
        public ILogger Logger { get; }
        public ILoggerFactory LoggerFactory { get; }
        public bool DisableResolverServiceConfig { get; }
        public LoadBalancerFactory[] LoadBalancerFactories { get; }

        // For unit tests.
        internal IReadOnlyList<Subchannel> GetSubchannels()
        {
            lock (_subchannels)
            {
                return _subchannels.ToArray();
            }
        }

        internal int GetNextId()
        {
            return Interlocked.Increment(ref _currentSubchannelId);
        }

        public void ConfigureBalancer(Func<IChannelControlHelper, LoadBalancer> configure)
        {
            _balancer = configure(this);
        }

        Subchannel IChannelControlHelper.CreateSubchannel(SubchannelOptions options)
        {
            var subchannel = new Subchannel(this, options.Addresses);
            subchannel.Transport = _subchannelTransportFactory.Create(subchannel);

            lock (_subchannels)
            {
                _subchannels.Add(subchannel);
            }

            return subchannel;
        }

        void IChannelControlHelper.RefreshResolver()
        {
            lock (_lock)
            {
                ConnectionManagerLog.ResolverRefreshRequested(Logger);

                if (_resolveTask == null || !_resolveTask.IsCompleted)
                {
                    _resolveTask = ResolveNowAsync(_cts.Token);
                }
                else
                {
                    ConnectionManagerLog.ResolverRefreshIgnored(Logger);
                }
            }
        }

        private async Task ResolveNowAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _resolver.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConnectionManagerLog.ResolverRefreshError(Logger, ex);
            }
        }

        private void OnResolverResult(ResolverResult result)
        {
            if (_balancer == null)
            {
                throw new InvalidOperationException($"Load balancer not configured.");
            }

            var channelStatus = result.Status;

            // https://github.com/grpc/proposal/blob/master/A21-service-config-error-handling.md
            // Additionally, only use resolved service config if not disabled.
            LoadBalancingConfig? loadBalancingConfig = null;
            if (!DisableResolverServiceConfig)
            {
                ServiceConfig? workingServiceConfig = null;
                if (result.ServiceConfig == null)
                {
                    // Step 4 and 5
                    if (result.ServiceConfigStatus == null)
                    {
                        // Step 5: Use default service config if none is provided.
                        _previousServiceConfig = DefaultServiceConfig;
                        workingServiceConfig = DefaultServiceConfig;
                    }
                    else
                    {
                        // Step 4
                        if (_previousServiceConfig == null)
                        {
                            // Step 4.ii: If no config was provided or set previously, then treat resolution as a failure.
                            channelStatus = result.ServiceConfigStatus.GetValueOrDefault();
                        }
                        else
                        {
                            // Step 4.i: Continue using previous service config if it was set and a new one is not provided.
                            workingServiceConfig = _previousServiceConfig;
                            ConnectionManagerLog.ResolverServiceConfigFallback(Logger, result.ServiceConfigStatus.GetValueOrDefault());
                        }
                    }
                }
                else
                {
                    // Step 3: Use provided service config if it is set.
                    workingServiceConfig = result.ServiceConfig;
                    _previousServiceConfig = result.ServiceConfig;
                }


                if (workingServiceConfig?.LoadBalancingConfigs.Count > 0)
                {
                    if (!ChildHandlerLoadBalancer.TryGetValidServiceConfigFactory(workingServiceConfig.LoadBalancingConfigs, LoadBalancerFactories, out loadBalancingConfig, out var _))
                    {
                        ConnectionManagerLog.ResolverUnsupportedLoadBalancingConfig(Logger, workingServiceConfig.LoadBalancingConfigs);
                    }
                }
            }
            else
            {
                if (result.ServiceConfig != null)
                {
                    ConnectionManagerLog.ResolverServiceConfigNotUsed(Logger);
                }
            }

            var state = new ChannelState(
                channelStatus,
                result.Addresses,
                loadBalancingConfig,
                BalancerAttributes.Empty);

            lock (_lock)
            {
                _balancer.UpdateChannelState(state);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _resolver.Dispose();
            _nextPickerLock.Dispose();
            lock (_lock)
            {
                _balancer?.Dispose();
            }
        }

        internal void OnSubchannelStateChange(Subchannel subchannel, ConnectivityState state, Status status)
        {
            if (state == ConnectivityState.Shutdown)
            {
                lock (_subchannels)
                {
                    var removed = _subchannels.Remove(subchannel);
                    Debug.Assert(removed);
                }
            }

            lock (_lock)
            {
                subchannel.RaiseStateChanged(state, status);
            }
        }

        public async Task ConnectAsync(bool waitForReady, CancellationToken cancellationToken)
        {
            await EnsureResolverStartedAsync().ConfigureAwait(false);

            if (!waitForReady || State == ConnectivityState.Ready)
            {
                return;
            }
            else
            {
                Task waitForReadyTask;
                lock (_lock)
                {
                    var state = State;
                    if (state == ConnectivityState.Ready)
                    {
                        return;
                    }

                    waitForReadyTask = WaitForStateChangedAsync(state, waitForState: ConnectivityState.Ready, cancellationToken);
                    _balancer?.RequestConnection();
                }

                await waitForReadyTask.ConfigureAwait(false);
            }
        }

        private Task EnsureResolverStartedAsync()
        {
            // Ensure that the resolver has started and has resolved at least once.
            // This ensures an inner load balancer has been created and is running.
            if (_resolverRefreshTask == null)
            {
                lock (_lock)
                {
                    if (_resolverRefreshTask == null)
                    {
                        _resolver.Start(OnResolverResult);
                        _resolverRefreshTask = _resolver.RefreshAsync(_cts.Token);
                    }
                }
            }

            return _resolverRefreshTask;
        }

        public void UpdateState(BalancerState state)
        {
            lock (_lock)
            {
                if (State != state.ConnectivityState)
                {
                    ConnectionManagerLog.ChannelStateUpdated(Logger, state.ConnectivityState);
                    State = state.ConnectivityState;

                    // Iterate in reverse to reduce shifting items in the list as watchers are removed.
                    for (var i = _stateWatchers.Count - 1; i >= 0; i--)
                    {
                        var stateWatcher = _stateWatchers[i];

                        if (stateWatcher.WaitForState == null || stateWatcher.WaitForState == State)
                        {
                            stateWatcher.Tcs.SetResult(null);
                            _stateWatchers.RemoveAt(i);
                        }
                    }
                }

                if (!Equals(_picker, state.Picker))
                {
                    ConnectionManagerLog.ChannelPickerUpdated(Logger);
                    _picker = state.Picker;
                    _nextPickerTcs.SetResult(state.Picker);
                    _nextPickerTcs = new TaskCompletionSource<SubchannelPicker>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public async
#if !NETSTANDARD2_0
            ValueTask<(Subchannel Subchannel, BalancerAddress Address, Action<CompletionContext> OnComplete)>
#else
            Task<(Subchannel Subchannel, DnsEndPoint Address, Action<CompleteContext> OnComplete)>
#endif
            PickAsync(PickContext context, bool waitForReady, CancellationToken cancellationToken)
        {
            SubchannelPicker? previousPicker = null;

            // Wait for a valid picker. When the client state changes a new picker will be returned.
            // Cancellation will break out of the loop. Typically cancellation will come from a
            // deadline specified for a call being exceeded.
            while (true)
            {
                var currentPicker = await GetPickerAsync(previousPicker, cancellationToken).ConfigureAwait(false);

                ConnectionManagerLog.PickStarted(Logger);
                var result = currentPicker.Pick(context);

                switch (result.Type)
                {
                    case PickResultType.Complete:
                        var subchannel = result.Subchannel!;
                        var address = subchannel.CurrentAddress;

                        if (address != null)
                        {
                            ConnectionManagerLog.PickResultSuccessful(Logger, subchannel.Id, address.EndPoint);
                            return (subchannel, address, result.Complete);
                        }
                        else
                        {
                            ConnectionManagerLog.PickResultSubchannelNoCurrentAddress(Logger, subchannel.Id);
                            previousPicker = currentPicker;
                        }
                        break;
                    case PickResultType.Queue:
                        ConnectionManagerLog.PickResultQueued(Logger);
                        previousPicker = currentPicker;
                        break;
                    case PickResultType.Fail:
                        if (waitForReady)
                        {
                            ConnectionManagerLog.PickResultFailureWithWaitForReady(Logger, result.Status);
                            previousPicker = currentPicker;
                        }
                        else
                        {
                            ConnectionManagerLog.PickResultFailure(Logger, result.Status);
                            throw new RpcException(result.Status);
                        }
                        break;
                    case PickResultType.Drop:
                        // Use metadata on the exception to signal the request was dropped.
                        // Metadata is checked by retry. If request was dropped then it isn't retried.
                        var metadata = new Metadata { new Metadata.Entry(GrpcProtocolConstants.DropRequestTrailer, bool.TrueString) };
                        throw new RpcException(result.Status, metadata);
                    default:
                        throw new InvalidOperationException($"Unexpected pick result type: {result.Type}");
                }
            }
        }

        private
#if !NETSTANDARD2_0
            ValueTask<SubchannelPicker>
#else
            Task<SubchannelPicker>
#endif
            GetPickerAsync(SubchannelPicker? currentPicker, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_picker != null && _picker != currentPicker)
                {
#if !NETSTANDARD2_0
                    return new ValueTask<SubchannelPicker>(_picker);
#else
                    return Task.FromResult<SubchannelPicker>(_picker);
#endif
                }
                else
                {
                    return GetNextPickerAsync(cancellationToken);
                }
            }
        }

        private async
#if !NETSTANDARD2_0
            ValueTask<SubchannelPicker>
#else
            Task<SubchannelPicker>
#endif
            GetNextPickerAsync(CancellationToken cancellationToken)
        {
            ConnectionManagerLog.PickWaiting(Logger);

            Debug.Assert(Monitor.IsEntered(_lock));

            var nextPickerTcs = _nextPickerTcs;

            await _nextPickerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (cancellationToken.Register(s => ((TaskCompletionSource<SubchannelPicker?>)s!).TrySetCanceled(), nextPickerTcs))
                {
                    var nextPicker = await nextPickerTcs.Task.ConfigureAwait(false);

                    lock (_lock)
                    {
                        _nextPickerTcs = new TaskCompletionSource<SubchannelPicker>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    return nextPicker;
                }
            }
            finally
            {
                _nextPickerLock.Release();
            }
        }

        internal Task WaitForStateChangedAsync(ConnectivityState lastObservedState, ConnectivityState? waitForState, CancellationToken cancellationToken)
        {
            StateWatcher? watcher;

            lock (_lock)
            {
                if (State != lastObservedState)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    // Minor optimization to check if we're already waiting for state change
                    // using the specified cancellation token.
                    foreach (var stateWatcher in _stateWatchers)
                    {
                        if (stateWatcher.CancellationToken == cancellationToken &&
                            stateWatcher.WaitForState == waitForState)
                        {
                            return stateWatcher.Tcs.Task;
                        }
                    }

                    watcher = new StateWatcher(
                        cancellationToken,
                        waitForState,
                        new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously));
                    _stateWatchers.Add(watcher);
                }
            }

            return WaitForStateChangedAsyncCore(watcher, cancellationToken);
        }

        private async Task WaitForStateChangedAsyncCore(StateWatcher watcher, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(OnCancellation, watcher))
            {
                await watcher.Tcs.Task.ConfigureAwait(false);
            }

            void OnCancellation(object? s)
            {
                lock (_lock)
                {
                    StateWatcher watcher = (StateWatcher)s!;
                    if (_stateWatchers.Remove(watcher))
                    {
                        watcher.Tcs.SetCanceled();
                    }
                }
            }
        }

        private record StateWatcher(CancellationToken CancellationToken, ConnectivityState? WaitForState, TaskCompletionSource<object?> Tcs);
    }

    internal static class ConnectionManagerLog
    {
        private static readonly Action<ILogger, Exception?> _resolverRefreshRequested =
            LoggerMessage.Define(LogLevel.Trace, new EventId(1, "ResolverRefreshRequested"), "Resolver refresh requested.");

        private static readonly Action<ILogger, Exception?> _resolverRefreshIgnored =
            LoggerMessage.Define(LogLevel.Trace, new EventId(2, "ResolverRefreshIgnored"), "Resolver refresh ignored because resolve is already in progress.");

        private static readonly Action<ILogger, Exception?> _resolverRefreshError =
            LoggerMessage.Define(LogLevel.Error, new EventId(3, "ResolverRefreshError"), "Error refreshing resolver.");

        private static readonly Action<ILogger, string, Exception?> _resolverUnsupportedLoadBalancingConfig =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, "ResolverUnsupportedLoadBalancingConfig"), "Service config returned by the resolver contains unsupported load balancer policies: {LoadBalancingConfigs}. Load balancer unchanged.");

        private static readonly Action<ILogger, Exception?> _resolverServiceConfigNotUsed =
            LoggerMessage.Define(LogLevel.Debug, new EventId(5, "ResolverServiceConfigNotUsed"), "Service config returned by the resolver not used.");

        private static readonly Action<ILogger, ConnectivityState, Exception?> _channelStateUpdated =
            LoggerMessage.Define<ConnectivityState>(LogLevel.Debug, new EventId(6, "ChannelStateUpdated"), "Channel state updated to {State}.");

        private static readonly Action<ILogger, Exception?> _channelPickerUpdated =
            LoggerMessage.Define(LogLevel.Debug, new EventId(7, "ChannelPickerUpdated"), "Channel picker updated.");

        private static readonly Action<ILogger, Exception?> _pickStarted =
            LoggerMessage.Define(LogLevel.Trace, new EventId(8, "PickStarted"), "Pick started.");

        private static readonly Action<ILogger, int, DnsEndPoint, Exception?> _pickResultSuccessful =
            LoggerMessage.Define<int, DnsEndPoint>(LogLevel.Debug, new EventId(9, "PickResultSuccessful"), "Successfully picked subchannel id '{SubchannelId}' with address {CurrentAddress}.");

        private static readonly Action<ILogger, int, Exception?> _pickResultSubchannelNoCurrentAddress =
            LoggerMessage.Define<int>(LogLevel.Debug, new EventId(10, "PickResultSubchannelNoCurrentAddress"), "Picked subchannel id '{SubchannelId}' doesn't have a current address.");

        private static readonly Action<ILogger, Exception?> _pickResultQueued =
            LoggerMessage.Define(LogLevel.Debug, new EventId(11, "PickResultQueued"), "Picked queued.");

        private static readonly Action<ILogger, Status, Exception?> _pickResultFailure =
            LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(12, "PickResultFailure"), "Picked failure with status: {Status}");

        private static readonly Action<ILogger, Status, Exception?> _pickResultFailureWithWaitForReady =
            LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(13, "PickResultFailureWithWaitForReady"), "Picked failure with status: {Status}. Retrying because wait for ready is enabled.");

        private static readonly Action<ILogger, Exception?> _pickWaiting =
            LoggerMessage.Define(LogLevel.Trace, new EventId(14, "PickWaiting"), "Waiting for a new picker.");

        private static readonly Action<ILogger, Status, Exception?> _resolverServiceConfigFallback =
            LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(15, "ResolverServiceConfigFallback"), "Falling back to previously loaded service config. Resolver failure when retreiving or parsing service config with status: {Status}");

        public static void ResolverRefreshRequested(ILogger logger)
        {
            _resolverRefreshRequested(logger, null);
        }

        public static void ResolverRefreshIgnored(ILogger logger)
        {
            _resolverRefreshIgnored(logger, null);
        }

        public static void ResolverRefreshError(ILogger logger, Exception ex)
        {
            _resolverRefreshError(logger, ex);
        }

        public static void ResolverUnsupportedLoadBalancingConfig(ILogger logger, IList<LoadBalancingConfig> loadBalancingConfigs)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var loadBalancingConfigText = string.Join(", ", loadBalancingConfigs.Select(c => $"'{c.PolicyName}'"));
                _resolverUnsupportedLoadBalancingConfig(logger, loadBalancingConfigText, null);
            }
        }

        public static void ResolverServiceConfigNotUsed(ILogger logger)
        {
            _resolverServiceConfigNotUsed(logger, null);
        }

        public static void ChannelStateUpdated(ILogger logger, ConnectivityState connectivityState)
        {
            _channelStateUpdated(logger, connectivityState, null);
        }

        public static void ChannelPickerUpdated(ILogger logger)
        {
            _channelPickerUpdated(logger, null);
        }

        public static void PickStarted(ILogger logger)
        {
            _pickStarted(logger, null);
        }

        public static void PickResultSuccessful(ILogger logger, int subchannelId, DnsEndPoint currentAddress)
        {
            _pickResultSuccessful(logger, subchannelId, currentAddress, null);
        }

        public static void PickResultSubchannelNoCurrentAddress(ILogger logger, int subchannelId)
        {
            _pickResultSubchannelNoCurrentAddress(logger, subchannelId, null);
        }

        public static void PickResultQueued(ILogger logger)
        {
            _pickResultQueued(logger, null);
        }

        public static void PickResultFailure(ILogger logger, Status status)
        {
            _pickResultFailure(logger, status, null);
        }

        public static void PickResultFailureWithWaitForReady(ILogger logger, Status status)
        {
            _pickResultFailureWithWaitForReady(logger, status, null);
        }

        public static void PickWaiting(ILogger logger)
        {
            _pickWaiting(logger, null);
        }

        public static void ResolverServiceConfigFallback(ILogger logger, Status status)
        {
            _resolverServiceConfigFallback(logger, status, null);
        }
    }
}
#endif
