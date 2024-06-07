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
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal;

internal sealed class ConnectionManager : IDisposable, IChannelControlHelper
{
    public static readonly BalancerAttributesKey<string> HostOverrideKey = new BalancerAttributesKey<string>("HostOverride");
    private static readonly ChannelIdProvider _channelIdProvider = new ChannelIdProvider();

    private readonly object _lock;
    internal readonly Resolver _resolver;
    private readonly ISubchannelTransportFactory _subchannelTransportFactory;
    private readonly List<Subchannel> _subchannels;
    private readonly List<StateWatcher> _stateWatchers;
    private readonly TaskCompletionSource<object?> _resolverStartedTcs;
    private readonly long _channelId;

    // Internal for testing
    internal LoadBalancer? _balancer;
    internal SubchannelPicker? _picker;
    // Cache picker wrapped in task once and reuse.
    private Task<SubchannelPicker>? _pickerTask;
    private bool _resolverStarted;
    private TaskCompletionSource<SubchannelPicker> _nextPickerTcs;
    private int _currentSubchannelId;
    private ServiceConfig? _previousServiceConfig;

    internal ConnectionManager(
        Resolver resolver,
        bool disableResolverServiceConfig,
        ILoggerFactory loggerFactory,
        IBackoffPolicyFactory backoffPolicyFactory,
        ISubchannelTransportFactory subchannelTransportFactory,
        LoadBalancerFactory[] loadBalancerFactories)
    {
        _lock = new object();
        _nextPickerTcs = new TaskCompletionSource<SubchannelPicker>(TaskCreationOptions.RunContinuationsAsynchronously);
        _resolverStartedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _channelId = _channelIdProvider.GetNextChannelId();

        Logger = loggerFactory.CreateLogger(typeof(ConnectionManager));
        LoggerFactory = loggerFactory;
        BackoffPolicyFactory = backoffPolicyFactory;
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
    public IBackoffPolicyFactory BackoffPolicyFactory { get; }
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

    internal string GetNextId()
    {
        var nextSubchannelId = Interlocked.Increment(ref _currentSubchannelId);
        return $"{_channelId}-{nextSubchannelId}";
    }

    public void ConfigureBalancer(Func<IChannelControlHelper, LoadBalancer> configure)
    {
        _balancer = configure(this);
    }

    Subchannel IChannelControlHelper.CreateSubchannel(SubchannelOptions options)
    {
        var subchannel = new Subchannel(this, options.Addresses);
        subchannel.SetTransport(_subchannelTransportFactory.Create(subchannel));

        lock (_subchannels)
        {
            _subchannels.Add(subchannel);
        }

        return subchannel;
    }

    void IChannelControlHelper.RefreshResolver()
    {
        _resolver.Refresh();
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
                    workingServiceConfig = new ServiceConfig();
                    _previousServiceConfig = workingServiceConfig;
                }
                else
                {
                    // Step 4
                    if (_previousServiceConfig == null)
                    {
                        // Step 4.ii: If no config was provided or set previously, then treat resolution as a failure.
                        channelStatus = result.ServiceConfigStatus.Value;
                    }
                    else
                    {
                        // Step 4.i: Continue using previous service config if it was set and a new one is not provided.
                        workingServiceConfig = _previousServiceConfig;
                        ConnectionManagerLog.ResolverServiceConfigFallback(Logger, result.ServiceConfigStatus.Value);
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
            _resolverStartedTcs.TrySetResult(null);
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
        if (!_resolverStarted)
        {
            lock (_lock)
            {
                if (!_resolverStarted)
                {
                    _resolver.Start(OnResolverResult);
                    _resolver.Refresh();

                    _resolverStarted = true;
                }
            }
        }

        return _resolverStartedTcs.Task;
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

                    // Trigger watcher if either:
                    // 1. Watcher is waiting for any state change.
                    // 2. The state change matches the watcher's.
                    if (stateWatcher.WaitForState == null || stateWatcher.WaitForState == State)
                    {
                        _stateWatchers.RemoveAt(i);
                        stateWatcher.Tcs.SetResult(null);
                    }
                }
            }

            if (!Equals(_picker, state.Picker))
            {
                ConnectionManagerLog.ChannelPickerUpdated(Logger);
                _picker = state.Picker;
                _pickerTask = Task.FromResult(state.Picker);
                _nextPickerTcs.SetResult(state.Picker);
                _nextPickerTcs = new TaskCompletionSource<SubchannelPicker>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    public async ValueTask<(Subchannel Subchannel, BalancerAddress Address, ISubchannelCallTracker? SubchannelCallTracker)> PickAsync(PickContext context, bool waitForReady, CancellationToken cancellationToken)
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
                    var (address, state) = subchannel.GetAddressAndState();

                    if (address != null)
                    {
                        if (state == ConnectivityState.Ready)
                        {
                            ConnectionManagerLog.PickResultSuccessful(Logger, subchannel.Id, address, subchannel.Transport.TransportStatus);
                            return (subchannel, address, result.SubchannelCallTracker);
                        }
                        else
                        {
                            ConnectionManagerLog.PickResultSubchannelNotReady(Logger, subchannel.Id, address, state);
                            previousPicker = currentPicker;
                        }
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

    private Task<SubchannelPicker> GetPickerAsync(SubchannelPicker? currentPicker, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_picker != null && _picker != currentPicker)
            {
                Debug.Assert(_pickerTask != null);
                return _pickerTask;
            }
            else
            {
                ConnectionManagerLog.PickWaiting(Logger);

                return _nextPickerTcs.Task.WaitAsync(cancellationToken);
            }
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

        return WaitForStateChangedAsyncCore(watcher);
    }

    private async Task WaitForStateChangedAsyncCore(StateWatcher watcher)
    {
        using (watcher.CancellationToken.Register(OnCancellation, watcher))
        {
            await watcher.Tcs.Task.ConfigureAwait(false);
        }
    }

    private void OnCancellation(object? s)
    {
        lock (_lock)
        {
            StateWatcher watcher = (StateWatcher)s!;
            if (_stateWatchers.Remove(watcher))
            {
                watcher.Tcs.SetCanceled(watcher.CancellationToken);
            }
        }
    }

    // Use a standard class for the watcher because:
    // 1. On cancellation, a watcher is removed from collection. Should use default Equals implementation. Record overrides Equals.
    // 2. This type is cast to object. A struct will box.
    private sealed class StateWatcher
    {
        public StateWatcher(CancellationToken cancellationToken, ConnectivityState? waitForState, TaskCompletionSource<object?> tcs)
        {
            CancellationToken = cancellationToken;
            WaitForState = waitForState;
            Tcs = tcs;
        }

        public CancellationToken CancellationToken { get; }
        public ConnectivityState? WaitForState { get; }
        public TaskCompletionSource<object?> Tcs { get; }
    }

    public void Dispose()
    {
        _resolver.Dispose();
        lock (_lock)
        {
            _balancer?.Dispose();

            // Cancel pending state watchers.
            // Iterate in reverse to reduce shifting items in the list as watchers are removed.
            for (var i = _stateWatchers.Count - 1; i >= 0; i--)
            {
                var stateWatcher = _stateWatchers[i];

                stateWatcher.Tcs.SetCanceled();
                _stateWatchers.RemoveAt(i);
            }
        }
    }
}

internal static class ConnectionManagerLog
{
    private static readonly Action<ILogger, string, Exception?> _resolverUnsupportedLoadBalancingConfig =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "ResolverUnsupportedLoadBalancingConfig"), "Service config returned by the resolver contains unsupported load balancer policies: {LoadBalancingConfigs}. Load balancer unchanged.");

    private static readonly Action<ILogger, Exception?> _resolverServiceConfigNotUsed =
        LoggerMessage.Define(LogLevel.Debug, new EventId(2, "ResolverServiceConfigNotUsed"), "Service config returned by the resolver not used.");

    private static readonly Action<ILogger, ConnectivityState, Exception?> _channelStateUpdated =
        LoggerMessage.Define<ConnectivityState>(LogLevel.Debug, new EventId(3, "ChannelStateUpdated"), "Channel state updated to {State}.");

    private static readonly Action<ILogger, Exception?> _channelPickerUpdated =
        LoggerMessage.Define(LogLevel.Debug, new EventId(4, "ChannelPickerUpdated"), "Channel picker updated.");

    private static readonly Action<ILogger, Exception?> _pickStarted =
        LoggerMessage.Define(LogLevel.Trace, new EventId(5, "PickStarted"), "Pick started.");

    private static readonly Action<ILogger, string, BalancerAddress, TransportStatus, Exception?> _pickResultSuccessful =
        LoggerMessage.Define<string, BalancerAddress, TransportStatus>(LogLevel.Debug, new EventId(6, "PickResultSuccessful"), "Successfully picked subchannel id '{SubchannelId}' with address {CurrentAddress}. Transport status: {TransportStatus}");

    private static readonly Action<ILogger, string, Exception?> _pickResultSubchannelNoCurrentAddress =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(7, "PickResultSubchannelNoCurrentAddress"), "Picked subchannel id '{SubchannelId}' doesn't have a current address.");

    private static readonly Action<ILogger, Exception?> _pickResultQueued =
        LoggerMessage.Define(LogLevel.Debug, new EventId(8, "PickResultQueued"), "Picked queued.");

    private static readonly Action<ILogger, Status, Exception?> _pickResultFailure =
        LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(9, "PickResultFailure"), "Picked failure with status: {Status}");

    private static readonly Action<ILogger, Status, Exception?> _pickResultFailureWithWaitForReady =
        LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(10, "PickResultFailureWithWaitForReady"), "Picked failure with status: {Status}. Retrying because wait for ready is enabled.");

    private static readonly Action<ILogger, Exception?> _pickWaiting =
        LoggerMessage.Define(LogLevel.Trace, new EventId(11, "PickWaiting"), "Waiting for a new picker.");

    private static readonly Action<ILogger, Status, Exception?> _resolverServiceConfigFallback =
        LoggerMessage.Define<Status>(LogLevel.Debug, new EventId(12, "ResolverServiceConfigFallback"), "Falling back to previously loaded service config. Resolver failure when retreiving or parsing service config with status: {Status}");

    private static readonly Action<ILogger, string, BalancerAddress, ConnectivityState, Exception?> _pickResultSubchannelNotReady =
        LoggerMessage.Define<string, BalancerAddress, ConnectivityState>(LogLevel.Debug, new EventId(13, "PickResultSubchannelNotReady"), "Picked subchannel id '{SubchannelId}' with address {CurrentAddress} doesn't have a ready state. Subchannel state: {State}");

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

    public static void PickResultSuccessful(ILogger logger, string subchannelId, BalancerAddress currentAddress, TransportStatus transportStatus)
    {
        _pickResultSuccessful(logger, subchannelId, currentAddress, transportStatus, null);
    }

    public static void PickResultSubchannelNoCurrentAddress(ILogger logger, string subchannelId)
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

    public static void PickResultSubchannelNotReady(ILogger logger, string subchannelId, BalancerAddress currentAddress, ConnectivityState state)
    {
        _pickResultSubchannelNotReady(logger, subchannelId, currentAddress, state, null);
    }
}
#endif
