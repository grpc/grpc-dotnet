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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
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
        internal readonly List<DnsEndPoint> _addresses;
        internal readonly object Lock;
        internal ISubchannelTransport Transport { get; set; } = default!;

        private readonly ConnectionManager _manager;
        private readonly ILogger _logger;

        private CancellationTokenSource? _connectCts;
        private ConnectivityState _state;
        private TaskCompletionSource<object?>? _delayInterruptTcs;

        /// <summary>
        /// Gets the current connected address.
        /// </summary>
        public DnsEndPoint? CurrentAddress => Transport.CurrentEndPoint;

        /// <summary>
        /// Gets the connectivity state.
        /// </summary>
        public ConnectivityState State => _state;

        /// <summary>
        /// Gets the metadata attributes.
        /// </summary>
        public BalancerAttributes Attributes { get; }

        internal Subchannel(ConnectionManager manager, IReadOnlyList<DnsEndPoint> addresses)
        {
            Lock = new object();
            _logger = manager.LoggerFactory.CreateLogger(GetType());

            _addresses = addresses.ToList();
            _manager = manager;
            Attributes = new BalancerAttributes();
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

        private sealed class StateChangedRegistration : IDisposable
        {
            private readonly Subchannel _subchannel;
            private readonly Action<SubchannelState> _callback;

            public StateChangedRegistration(Subchannel subchannel, Action<SubchannelState> callback)
            {
                _subchannel = subchannel;
                _callback = callback;
            }

            public void Invoke(SubchannelState state)
            {
                _callback(state);
            }

            public void Dispose()
            {
                _subchannel._stateChangedRegistrations.Remove(this);
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
        public void UpdateAddresses(IReadOnlyList<DnsEndPoint> addresses)
        {
            var requireReconnect = false;
            lock (Lock)
            {
                if (_addresses.SequenceEqual(addresses))
                {
                    // Don't do anything if new addresses match existing addresses.
                    return;
                }

                _addresses.Clear();
                _addresses.AddRange(addresses);

                switch (_state)
                {
                    case ConnectivityState.Idle:
                        break;
                    case ConnectivityState.Connecting:
                    case ConnectivityState.TransientFailure:
                        _logger.LogInformation($"Subchannel is connecting when its addresses are updated. Restart connect.");
                        requireReconnect = true;
                        break;
                    case ConnectivityState.Ready:
                        _logger.LogInformation($"Subchannel current endpoint {CurrentAddress} is not in the updated addresses.");
                        requireReconnect = (CurrentAddress != null && !_addresses.Contains(CurrentAddress));
                        break;
                    case ConnectivityState.Shutdown:
                        throw new InvalidOperationException("Subchannel has been shutdown.");
                    default:
                        throw new ArgumentOutOfRangeException("state", _state, "Unexpected state.");
                }

            }
            if (requireReconnect)
            {
                _connectCts?.Cancel();
                Transport.Disconnect();
                RequestConnection();
            }
        }

        /// <summary>
        /// Creates a connection (aka transport), if there isn't an active one.
        /// </summary>
        public void RequestConnection()
        {
            _logger.LogInformation("Connection requested.");

            lock (Lock)
            {
                switch (_state)
                {
                    case ConnectivityState.Idle:
                        // Only start connecting underlying transport if in an idle state.
                        UpdateConnectivityState(ConnectivityState.Connecting);
                        break;
                    case ConnectivityState.Connecting:
                    case ConnectivityState.Ready:
                    case ConnectivityState.TransientFailure:
                        _logger.LogInformation($"Subchannel is not idle: {_state}");

                        // We're already attempting to connect to the transport.
                        // If the connection is waiting in a delayed backoff then interrupt
                        // the delay and immediately retry connection.
                        _delayInterruptTcs?.TrySetResult(null);
                        return;
                    case ConnectivityState.Shutdown:
                        throw new InvalidOperationException("Subchannel has been shutdown.");
                    default:
                        throw new ArgumentOutOfRangeException("state", _state, "Unexpected state.");
                }
            }

            _ = ConnectTransportAsync();
        }

        private async Task ConnectTransportAsync()
        {
            // There shouldn't be a previous connect in progress, but cancel the CTS to ensure they're no longer running.
            _connectCts?.Cancel();

            _connectCts = new CancellationTokenSource();

            const int InitialBackOffMs = 1000;

            try
            {
                _logger.LogInformation("Connecting to transport.");

                var backoffMs = InitialBackOffMs;
                for (var attempt = 0; ; attempt++)
                {
                    lock (Lock)
                    {
                        if (_state == ConnectivityState.Shutdown)
                        {
                            return;
                        }
                    }

                    if (await Transport.TryConnectAsync(_connectCts.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    _connectCts.Token.ThrowIfCancellationRequested();

                    _delayInterruptTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var delayCts = new CancellationTokenSource();

                    _logger.LogInformation($"Connect failed. Back off: {backoffMs}ms");
                    var completedTask = await Task.WhenAny(Task.Delay(backoffMs, delayCts.Token), _delayInterruptTcs.Task).ConfigureAwait(false);

                    if (completedTask != _delayInterruptTcs.Task)
                    {
                        // Task.Delay won. Check CTS to see if it won because of cancellation.
                        delayCts.Token.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        // Delay interrupt was triggered. Reset back-off.
                        backoffMs = InitialBackOffMs;

                        // Cancel the Task.Delay that's no longer needed.
                        // https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/519ef7d231c01116f02bc04354816a735f2a36b6/AsyncGuidance.md#using-a-timeout
                        delayCts.Cancel();
                    }

                    // Exponential backoff with max.
                    backoffMs = (int)Math.Min(backoffMs * 1.6, 1000 * 120);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Connect canceled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while connecting to transport.");

                UpdateConnectivityState(ConnectivityState.TransientFailure);
            }
        }

        internal bool UpdateConnectivityState(ConnectivityState state, Status? status = null)
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
            _manager.OnSubchannelStateChange(this, state, status ?? Status.DefaultSuccess);
            return true;
        }

        internal void RaiseStateChanged(ConnectivityState state, Status status)
        {
            _logger.LogInformation("Subchannel state change: " + this + " " + state);
            if (_stateChangedRegistrations.Count > 0)
            {
                var subchannelState = new SubchannelState(state, status);
                foreach (var registration in _stateChangedRegistrations)
                {
                    registration.Invoke(subchannelState);
                }
            }
        }

        /// <inheritdocs />
        public override string ToString()
        {
            lock (Lock)
            {
                return string.Join(", ", _addresses);
            }
        }

        /// <summary>
        /// Returns the addresses that this subchannel is bound to.
        /// </summary>
        /// <returns>The addresses that this subchannel is bound to.</returns>
        public IReadOnlyList<DnsEndPoint> GetAddresses()
        {
            lock (Lock)
            {
                return _addresses.ToArray();
            }
        }

        /// <summary>
        /// Disposes the <see cref="Subchannel"/>.
        /// The subchannel <see cref="State"/> is updated to <see cref="ConnectivityState.Shutdown"/>.
        /// After dispose the subchannel should no longer be returned by the latest <see cref="SubchannelPicker"/>.
        /// </summary>
        public void Dispose()
        {
            UpdateConnectivityState(ConnectivityState.Shutdown);
            _stateChangedRegistrations.Clear();
            Transport.Dispose();
            _connectCts?.Cancel();
        }
    }
}
#endif
