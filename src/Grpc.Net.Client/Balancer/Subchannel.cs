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
using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer;

/// <summary>
/// Represents a logical connection. A subchannel is created with one or more addresses to equivalent servers.
/// <para>
/// A subchannel maintains at most one physical connection (aka transport) for sending new gRPC calls.
/// If there isn't an active transport, and a call is assigned to the subchannel, it will create
/// a new transport. A transport won't be created otherwise unless <see cref="RequestConnection"/>
/// is called to create a transport if there isn't any.
/// </para>
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
public sealed class Subchannel : IDisposable
{
    internal readonly List<BalancerAddress> _addresses;
    internal readonly object Lock;
    internal ISubchannelTransport Transport => _transport;
    internal string Id { get; }

    /// <summary>
    /// Connectivity state is internal rather than public because it can be updated by multiple threads while
    /// a load balancer is building the picker.
    /// Load balancers that care about multiple subchannels should track state by subscribing to
    /// Subchannel.OnStateChanged and storing results.
    /// </summary>
    internal ConnectivityState State => _state;

    internal readonly ConnectionManager _manager;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectSemaphore;

    private ISubchannelTransport _transport = default!;
    private ConnectContext? _connectContext;
    private ConnectivityState _state;
    private TaskCompletionSource<object?>? _delayInterruptTcs;
    private int _currentRegistrationId;

    /// <summary>
    /// Gets the current connected address.
    /// </summary>
    public BalancerAddress? CurrentAddress
    {
        get
        {
            if (_transport.CurrentEndPoint is { } ep)
            {
                lock (Lock)
                {
                    return GetAddressByEndpoint(_addresses, ep);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the metadata attributes.
    /// </summary>
    public BalancerAttributes Attributes { get; }

    internal (BalancerAddress? Address, ConnectivityState State) GetAddressAndState()
    {
        lock (Lock)
        {
            return (CurrentAddress, State);
        }
    }

    internal Subchannel(ConnectionManager manager, IReadOnlyList<BalancerAddress> addresses)
    {
        Lock = new object();
        _logger = manager.LoggerFactory.CreateLogger(GetType());
        _connectSemaphore = new SemaphoreSlim(1);

        Id = manager.GetNextId();
        _addresses = addresses.ToList();
        _manager = manager;
        Attributes = new BalancerAttributes();

        SubchannelLog.SubchannelCreated(_logger, Id, addresses);
    }

    internal void SetTransport(ISubchannelTransport transport)
    {
        _transport = transport;
    }

    private readonly List<StateChangedRegistration> _stateChangedRegistrations = new List<StateChangedRegistration>();

    /// <summary>
    /// Registers a callback that will be invoked this subchannel's state changes.
    /// </summary>
    /// <param name="callback">The callback that will be invoked when the subchannel's state changes.</param>
    /// <returns>A subscription that can be disposed to unsubscribe from state changes.</returns>
    public IDisposable OnStateChanged(Action<SubchannelState> callback)
    {
        var registration = new StateChangedRegistration(this, callback);
        _stateChangedRegistrations.Add(registration);

        return registration;
    }

    private string GetNextRegistrationId()
    {
        var registrationId = Interlocked.Increment(ref _currentRegistrationId);
        return Id + "-" + registrationId;
    }

    private sealed class StateChangedRegistration : IDisposable
    {
        private readonly Subchannel _subchannel;
        private readonly Action<SubchannelState> _callback;

        public string RegistrationId { get; }

        public StateChangedRegistration(Subchannel subchannel, Action<SubchannelState> callback)
        {
            _subchannel = subchannel;
            _callback = callback;
            RegistrationId = subchannel.GetNextRegistrationId();

            SubchannelLog.StateChangedRegistrationCreated(_subchannel._logger, _subchannel.Id, RegistrationId);
        }

        public void Invoke(SubchannelState state)
        {
            SubchannelLog.ExecutingStateChangedRegistration(_subchannel._logger, _subchannel.Id, RegistrationId);
            _callback(state);
        }

        public void Dispose()
        {
            if (_subchannel._stateChangedRegistrations.Remove(this))
            {
                SubchannelLog.StateChangedRegistrationRemoved(_subchannel._logger, _subchannel.Id, RegistrationId);
            }
        }
    }

    /// <summary>
    /// Replaces the existing addresses used with this <see cref="Subchannel"/>.
    /// <para>
    /// If the subchannel has an active connection and the new addresses contain the connected address
    /// then the connection is reused. Otherwise the subchannel will reconnect.
    /// </para>
    /// </summary>
    /// <param name="addresses"></param>
    public void UpdateAddresses(IReadOnlyList<BalancerAddress> addresses)
    {
        var requireReconnect = false;
        lock (Lock)
        {
            if (_addresses.SequenceEqual(addresses, BalancerAddressEqualityComparer.Instance))
            {
                // Don't do anything if new addresses match existing addresses.
                return;
            }

            SubchannelLog.AddressesUpdated(_logger, Id, addresses);

            // Get a copy of the current address before updating addresses.
            // Updating addresses to not contain this value changes the property to return null.
            var currentAddress = CurrentAddress;

            _addresses.Clear();
            _addresses.AddRange(addresses);

            switch (_state)
            {
                case ConnectivityState.Idle:
                    break;
                case ConnectivityState.Connecting:
                case ConnectivityState.TransientFailure:
                    SubchannelLog.AddressesUpdatedWhileConnecting(_logger, Id);
                    requireReconnect = true;
                    break;
                case ConnectivityState.Ready:
                    // Check if the subchannel is connected to an address that's not longer present.
                    // In this situation require the subchannel to reconnect to a new address.
                    if (currentAddress != null)
                    {
                        if (GetAddressByEndpoint(_addresses, currentAddress.EndPoint) is null)
                        {
                            SubchannelLog.ConnectedAddressNotInUpdatedAddresses(_logger, Id, currentAddress);
                            requireReconnect = true;
                        }
                    }
                    break;
                case ConnectivityState.Shutdown:
                    throw new InvalidOperationException($"Subchannel id '{Id}' has been shutdown.");
                default:
                    throw new ArgumentOutOfRangeException("state", _state, "Unexpected state.");
            }
        }

        if (requireReconnect)
        {
            lock (Lock)
            {
                CancelInProgressConnectUnsynchronized();
            }
            _transport.Disconnect();
            RequestConnection();
        }
    }

    /// <summary>
    /// Creates a connection (aka transport), if there isn't an active one.
    /// </summary>
    public void RequestConnection()
    {
        lock (Lock)
        {
            switch (_state)
            {
                case ConnectivityState.Idle:
                    SubchannelLog.ConnectionRequested(_logger, Id);

                    // Only start connecting underlying transport if in an idle state.
                    UpdateConnectivityState(ConnectivityState.Connecting, "Connection requested.");
                    break;
                case ConnectivityState.Connecting:
                case ConnectivityState.Ready:
                case ConnectivityState.TransientFailure:
                    SubchannelLog.ConnectionRequestedInNonIdleState(_logger, Id, _state);

                    // We're already attempting to connect to the transport.
                    // If the connection is waiting in a delayed backoff then interrupt
                    // the delay and immediately retry connection.
                    _delayInterruptTcs?.TrySetResult(null);
                    return;
                case ConnectivityState.Shutdown:
                    throw new InvalidOperationException($"Subchannel id '{Id}' has been shutdown.");
                default:
                    throw new ArgumentOutOfRangeException("state", _state, "Unexpected state.");
            }
        }

        // Don't capture the current ExecutionContext and its AsyncLocals onto the connect
        var restoreFlow = false;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            ExecutionContext.SuppressFlow();
            restoreFlow = true;
        }

        _ = Task.Run(ConnectTransportAsync);

        // Restore the current ExecutionContext
        if (restoreFlow)
        {
            ExecutionContext.RestoreFlow();
        }
    }

    private void CancelInProgressConnectUnsynchronized()
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        if (_connectContext != null && !_connectContext.Disposed)
        {
            SubchannelLog.CancelingConnect(_logger, Id);

            // Cancel connect cancellation token.
            _connectContext.CancelConnect();
            _connectContext.Dispose();
        }

        _delayInterruptTcs?.TrySetResult(null);
    }

    private ConnectContext GetConnectContextUnsynchronized()
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        // There shouldn't be a previous connect in progress, but cancel the CTS to ensure they're no longer running.
        CancelInProgressConnectUnsynchronized();

        var connectContext = _connectContext = new ConnectContext(_transport.ConnectTimeout ?? Timeout.InfiniteTimeSpan);
        return connectContext;
    }

    private async Task ConnectTransportAsync()
    {
        ConnectContext connectContext;
        Task? waitSemaporeTask = null;
        lock (Lock)
        {
            // Don't start connecting if the subchannel has been shutdown. Transport/semaphore will be disposed if shutdown.
            if (_state == ConnectivityState.Shutdown)
            {
                return;
            }

            connectContext = GetConnectContextUnsynchronized();

            // Use a semaphore to limit one connection attempt at a time. This is done to prevent a race conditional where a canceled connect
            // overwrites the status of a successful connect.
            //
            // Try to get semaphore without waiting. If semaphore is already taken then start a task to wait for it to be released.
            // Start this inside a lock to make sure subchannel isn't shutdown before waiting for semaphore.
            if (!_connectSemaphore.Wait(0))
            {
                SubchannelLog.QueuingConnect(_logger, Id);
                waitSemaporeTask = _connectSemaphore.WaitAsync(connectContext.CancellationToken);
            }
        }

        if (waitSemaporeTask != null)
        {
            try
            {
                await waitSemaporeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Canceled while waiting for semaphore.
                return;
            }
        }

        try
        {
            var backoffPolicy = _manager.BackoffPolicyFactory.Create();

            SubchannelLog.ConnectingTransport(_logger, Id);

            for (var attempt = 0; ; attempt++)
            {
                lock (Lock)
                {
                    if (_state == ConnectivityState.Shutdown)
                    {
                        return;
                    }
                }

                switch (await _transport.TryConnectAsync(connectContext, attempt).ConfigureAwait(false))
                {
                    case ConnectResult.Success:
                        return;
                    case ConnectResult.Timeout:
                        // Reset connectivity state back to idle so that new calls try to reconnect.
                        UpdateConnectivityState(ConnectivityState.Idle, new Status(StatusCode.Unavailable, "Timeout connecting to subchannel."));
                        return;
                    case ConnectResult.Failure:
                    default:
                        break;
                }

                connectContext.CancellationToken.ThrowIfCancellationRequested();

                _delayInterruptTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var delayCts = new CancellationTokenSource();

                var backoffTicks = backoffPolicy.NextBackoff().Ticks;
                // Task.Delay supports up to Int32.MaxValue milliseconds.
                // Note that even if the maximum backoff is configured to this maximum, the jitter could push it over the limit.
                // Force an upper bound here to ensure an unsupported backoff is never used.
                backoffTicks = Math.Min(backoffTicks, TimeSpan.TicksPerMillisecond * int.MaxValue);
                
                var backkoff = TimeSpan.FromTicks(backoffTicks);
                SubchannelLog.StartingConnectBackoff(_logger, Id, backkoff);
                var completedTask = await Task.WhenAny(Task.Delay(backkoff, delayCts.Token), _delayInterruptTcs.Task).ConfigureAwait(false);

                if (completedTask != _delayInterruptTcs.Task)
                {
                    // Task.Delay won. Check CTS to see if it won because of cancellation.
                    delayCts.Token.ThrowIfCancellationRequested();
                    SubchannelLog.ConnectBackoffComplete(_logger, Id);
                }
                else
                {
                    SubchannelLog.ConnectBackoffInterrupted(_logger, Id);

                    // Cancel the Task.Delay that's no longer needed.
                    // https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/519ef7d231c01116f02bc04354816a735f2a36b6/AsyncGuidance.md#using-a-timeout
                    delayCts.Cancel();

                    // Check to connect context token to see if the delay was interrupted because of a connect cancellation.
                    connectContext.CancellationToken.ThrowIfCancellationRequested();

                    // Delay interrupt was triggered. Reset back-off.
                    backoffPolicy = _manager.BackoffPolicyFactory.Create();
                }
            }
        }
        catch (OperationCanceledException)
        {
            SubchannelLog.ConnectCanceled(_logger, Id);
        }
        catch (Exception ex)
        {
            SubchannelLog.ConnectError(_logger, Id, ex);

            UpdateConnectivityState(ConnectivityState.TransientFailure, new Status(StatusCode.Unavailable, "Error connecting to subchannel.", ex));
        }
        finally
        {
            lock (Lock)
            {
                // Dispose context because it might have been created with a connect timeout.
                // Want to clean up the connect timeout timer.
                connectContext.Dispose();

                // Subchannel could have been disposed while connect is running.
                // If subchannel is shutting down then don't release semaphore to avoid ObjectDisposedException.
                if (_state != ConnectivityState.Shutdown)
                {
                    _connectSemaphore.Release();
                }
            }
        }
    }

    internal bool UpdateConnectivityState(ConnectivityState state, string successDetail)
    {
        return UpdateConnectivityState(state, new Status(StatusCode.OK, successDetail));
    }

    internal bool UpdateConnectivityState(ConnectivityState state, Status status)
    {
        lock (Lock)
        {
            // Don't update subchannel state if the state is the same or the subchannel has been shutdown.
            //
            // This could happen when:
            // 1. Start trying to connect with a subchannel.
            // 2. Address resolver updates and subchannel address is no longer there and subchannel is shutdown.
            // 3. Connection attempt fails and tries to update subchannel state.
            if (_state == state || _state == ConnectivityState.Shutdown)
            {
                return false;
            }
            _state = state;
        }
        
        // Notify channel outside of lock to avoid deadlocks.
        _manager.OnSubchannelStateChange(this, state, status);
        return true;
    }

    internal void RaiseStateChanged(ConnectivityState state, Status status)
    {
        SubchannelLog.SubchannelStateChanged(_logger, Id, state, status);

        if (_stateChangedRegistrations.Count > 0)
        {
            var subchannelState = new SubchannelState(state, status);
            foreach (var registration in _stateChangedRegistrations)
            {
                registration.Invoke(subchannelState);
            }
        }
        else
        {
            SubchannelLog.NoStateChangedRegistrations(_logger, Id);
        }
    }

    private static BalancerAddress? GetAddressByEndpoint(List<BalancerAddress> addresses, DnsEndPoint endPoint)
    {
        foreach (var a in addresses)
        {
            if (a.EndPoint.Equals(endPoint))
            {
                return a;
            }
        }

        return null;
    }

    /// <inheritdocs />
    public override string ToString()
    {
        lock (Lock)
        {
            return $"Id: {Id}, Addresses: {string.Join(", ", _addresses)}, State: {_state}, Current address: {CurrentAddress}";
        }
    }

    /// <summary>
    /// Returns the addresses that this subchannel is bound to.
    /// </summary>
    /// <returns>The addresses that this subchannel is bound to.</returns>
    public IReadOnlyList<BalancerAddress> GetAddresses()
    {
        lock (Lock)
        {
            return _addresses.ToArray();
        }
    }

    /// <summary>
    /// Disposes the <see cref="Subchannel"/>.
    /// The subchannel state is updated to <see cref="ConnectivityState.Shutdown"/>.
    /// After dispose the subchannel should no longer be returned by the latest <see cref="SubchannelPicker"/>.
    /// </summary>
    public void Dispose()
    {
        UpdateConnectivityState(ConnectivityState.Shutdown, "Subchannel disposed.");

        foreach (var registration in _stateChangedRegistrations)
        {
            SubchannelLog.StateChangedRegistrationRemoved(_logger, Id, registration.RegistrationId);
        }
        _stateChangedRegistrations.Clear();

        lock (Lock)
        {
            CancelInProgressConnectUnsynchronized();
            _transport.Dispose();
            _connectSemaphore.Dispose();
        }
    }
}

internal static partial class SubchannelLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "SubchannelCreated", Message = "Subchannel id '{SubchannelId}' created with addresses: {Addresses}")]
    private static partial void SubchannelCreated(ILogger logger, string subchannelId, string addresses);

    public static void SubchannelCreated(ILogger logger, string subchannelId, IReadOnlyList<BalancerAddress> addresses)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var addressesText = string.Join(", ", addresses.Select(a => a.EndPoint.Host + ":" + a.EndPoint.Port));
            SubchannelCreated(logger, subchannelId, addressesText);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2, EventName = "AddressesUpdatedWhileConnecting", Message = "Subchannel id '{SubchannelId}' is connecting when its addresses are updated. Restarting connect.")]
    public static partial void AddressesUpdatedWhileConnecting(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 3, EventName = "ConnectedAddressNotInUpdatedAddresses", Message = "Subchannel id '{SubchannelId}' current address '{CurrentAddress}' is not in the updated addresses.")]
    public static partial void ConnectedAddressNotInUpdatedAddresses(ILogger logger, string subchannelId, BalancerAddress currentAddress);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 4, EventName = "ConnectionRequested", Message = "Subchannel id '{SubchannelId}' connection requested.")]
    public static partial void ConnectionRequested(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 5, EventName = "ConnectionRequestedInNonIdleState", Message = "Subchannel id '{SubchannelId}' connection requested in non-idle state of {State}.")]
    public static partial void ConnectionRequestedInNonIdleState(ILogger logger, string subchannelId, ConnectivityState state);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6, EventName = "ConnectingTransport", Message = "Subchannel id '{SubchannelId}' connecting to transport.")]
    public static partial void ConnectingTransport(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 7, EventName = "StartingConnectBackoff", Message = "Subchannel id '{SubchannelId}' starting connect backoff of {BackoffDuration}.")]
    public static partial void StartingConnectBackoff(ILogger logger, string subchannelId, TimeSpan BackoffDuration);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 8, EventName = "ConnectBackoffInterrupted", Message = "Subchannel id '{SubchannelId}' connect backoff interrupted.")]
    public static partial void ConnectBackoffInterrupted(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 9, EventName = "ConnectCanceled", Message = "Subchannel id '{SubchannelId}' connect canceled.")]
    public static partial void ConnectCanceled(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Error, EventId = 10, EventName = "ConnectError", Message = "Subchannel id '{SubchannelId}' unexpected error while connecting to transport.")]
    public static partial void ConnectError(ILogger logger, string subchannelId, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 11, EventName = "SubchannelStateChanged", Message = "Subchannel id '{SubchannelId}' state changed to {State}. Detail: '{Detail}'.")]
    private static partial void SubchannelStateChanged(ILogger logger, string subchannelId, ConnectivityState state, string Detail, Exception? DebugException);

    public static void SubchannelStateChanged(ILogger logger, string subchannelId, ConnectivityState state, Status status)
    {
        SubchannelStateChanged(logger, subchannelId, state, status.Detail, status.DebugException);
    }

    [LoggerMessage(Level = LogLevel.Trace, EventId = 12, EventName = "StateChangedRegistrationCreated", Message = "Subchannel id '{SubchannelId}' state changed registration '{RegistrationId}' created.")]
    public static partial void StateChangedRegistrationCreated(ILogger logger, string subchannelId, string registrationId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 13, EventName = "StateChangedRegistrationRemoved", Message = "Subchannel id '{SubchannelId}' state changed registration '{RegistrationId}' removed.")]
    public static partial void StateChangedRegistrationRemoved(ILogger logger, string subchannelId, string registrationId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 14, EventName = "ExecutingStateChangedRegistration", Message = "Subchannel id '{SubchannelId}' executing state changed registration '{RegistrationId}'.")]
    public static partial void ExecutingStateChangedRegistration(ILogger logger, string subchannelId, string registrationId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 15, EventName = "NoStateChangedRegistrations", Message = "Subchannel id '{SubchannelId}' has no state changed registrations.")]
    public static partial void NoStateChangedRegistrations(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 16, EventName = "SubchannelPreserved", Message = "Subchannel id '{SubchannelId}' matches address '{Address}' and is preserved.")]
    public static partial void SubchannelPreserved(ILogger logger, string subchannelId, BalancerAddress address);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 17, EventName = "CancelingConnect", Message = "Subchannel id '{SubchannelId}' canceling connect.")]
    public static partial void CancelingConnect(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 18, EventName = "ConnectBackoffComplete", Message = "Subchannel id '{SubchannelId}' connect backoff complete.")]
    public static partial void ConnectBackoffComplete(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 19, EventName = "AddressesUpdated", Message = "Subchannel id '{SubchannelId}' updated with addresses: {Addresses}")]
    private static partial void AddressesUpdated(ILogger logger, string subchannelId, string addresses);
    public static void AddressesUpdated(ILogger logger, string subchannelId, IReadOnlyList<BalancerAddress> addresses)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var addressesText = string.Join(", ", addresses.Select(a => a.EndPoint.Host + ":" + a.EndPoint.Port));
            AddressesUpdated(logger, subchannelId, addressesText);
        }
    }
    [LoggerMessage(Level = LogLevel.Debug, EventId = 20, EventName = "QueuingConnect", Message = "Subchannel id '{SubchannelId}' queuing connect because a connect is already in progress.")]
    public static partial void QueuingConnect(ILogger logger, string subchannelId);
}
#endif
