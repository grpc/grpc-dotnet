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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal
{
#if NET5_0_OR_GREATER
    /// <summary>
    /// Transport that makes it possible to monitor connectivity state while using HttpClient.
    /// 
    /// Features:
    /// 1. When a connection is requested the transport creates a Socket and connects to the server.
    ///    The socket is used with the first stream created by SocketsHttpHandler.ConnectCallback.
    ///    The transport keeps track of the socket or the streams in use to determine if the connection
    ///    is ready. Connectivity API features require knowing whether there is a connection available.
    ///    A limitation of the .NET support is only socket connectivity to the server is tracked.
    ///    This transport is unable to check whether TLS and HTTP is succcessfully negotiated.
    /// 2. Transport supports multiple addresses. When connecting it will iterate through the addresses,
    ///    attempting to connect to each one.
    /// </summary>
    internal class SocketConnectivitySubchannelTransport : ISubchannelTransport, IDisposable
    {
        internal record struct ActiveStream(BalancerAddress Address, Socket Socket, Stream? Stream);

        private readonly ILogger _logger;
        private readonly Subchannel _subchannel;
        private readonly TimeSpan _socketPingInterval;
        private readonly List<ActiveStream> _activeStreams;
        private readonly Timer _socketConnectedTimer;

        private int _lastEndPointIndex;
        internal Socket? _initialSocket;
        private BalancerAddress? _initialSocketAddress;
        private bool _disposed;
        private BalancerAddress? _currentAddress;

        public SocketConnectivitySubchannelTransport(Subchannel subchannel, TimeSpan socketPingInterval, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SocketConnectivitySubchannelTransport>();
            _subchannel = subchannel;
            _socketPingInterval = socketPingInterval;
            _activeStreams = new List<ActiveStream>();
            _socketConnectedTimer = new Timer(OnCheckSocketConnection, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public object Lock => _subchannel.Lock;
        public BalancerAddress? CurrentAddress => _currentAddress;
        public bool HasStream { get; }

        // For testing. Take a copy under lock for thread-safety.
        internal IReadOnlyList<ActiveStream> GetActiveStreams()
        {
            lock (Lock)
            {
                return _activeStreams.ToList();
            }
        }

        public void Disconnect()
        {
            lock (Lock)
            {
                if (_disposed)
                {
                    return;
                }

                DisconnectUnsynchronized();
                _socketConnectedTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
            }
            _subchannel.UpdateConnectivityState(ConnectivityState.Idle, "Disconnected.");
        }

        private void DisconnectUnsynchronized()
        {
            Debug.Assert(Monitor.IsEntered(Lock));
            Debug.Assert(!_disposed);

            _initialSocket?.Dispose();
            _initialSocket = null;
            _initialSocketAddress = null;
            _lastEndPointIndex = 0;
            _currentAddress = null;
        }

        public async ValueTask<bool> TryConnectAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(CurrentAddress == null);

            // Addresses could change while connecting. Make a copy of the subchannel's addresses.
            var addresses = _subchannel.GetAddresses();

            // Loop through endpoints and attempt to connect.
            Exception? firstConnectionError = null;

            for (var i = 0; i < addresses.Count; i++)
            {
                var currentIndex = (i + _lastEndPointIndex) % addresses.Count;
                var currentAddress = addresses[currentIndex];

                Socket socket;

                socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                _subchannel.UpdateConnectivityState(ConnectivityState.Connecting, "Connecting to socket.");

                try
                {
                    SocketConnectivitySubchannelTransportLog.ConnectingSocket(_logger, _subchannel.Id, currentAddress);
                    await socket.ConnectAsync(currentAddress.EndPoint, cancellationToken).ConfigureAwait(false);
                    SocketConnectivitySubchannelTransportLog.ConnectedSocket(_logger, _subchannel.Id, currentAddress);

                    lock (Lock)
                    {
                        _currentAddress = currentAddress;
                        _lastEndPointIndex = currentIndex;
                        _initialSocket = socket;
                        _initialSocketAddress = currentAddress;
                        _socketConnectedTimer.Change(_socketPingInterval, _socketPingInterval);
                    }

                    _subchannel.UpdateConnectivityState(ConnectivityState.Ready, "Successfully connected to socket.");
                    return true;
                }
                catch (Exception ex)
                {
                    SocketConnectivitySubchannelTransportLog.ErrorConnectingSocket(_logger, _subchannel.Id, currentAddress, ex);

                    if (firstConnectionError == null)
                    {
                        firstConnectionError = ex;
                    }
                }
            }

            // All connections failed
            _subchannel.UpdateConnectivityState(
                ConnectivityState.TransientFailure,
                new Status(StatusCode.Unavailable, "Error connecting to subchannel.", firstConnectionError));
            lock (Lock)
            {
                if (!_disposed)
                {
                    _socketConnectedTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
                }
            }
            return false;
        }

        private async void OnCheckSocketConnection(object? state)
        {
            try
            {
                var socket = _initialSocket;
                if (socket != null)
                {
                    CompatibilityHelpers.Assert(_initialSocketAddress != null);

                    var closeSocket = false;
                    Exception? sendException = null;
                    try
                    {
                        // Check the socket is still valid by doing a zero byte send.
                        SocketConnectivitySubchannelTransportLog.CheckingSocket(_logger, _subchannel.Id, _initialSocketAddress);
                        await socket.SendAsync(Array.Empty<byte>(), SocketFlags.None).ConfigureAwait(false);

                        // Also poll socket to check if it can be read from.
                        closeSocket = IsSocketInBadState(socket);
                    }
                    catch (Exception ex)
                    {
                        closeSocket = true;
                        sendException = ex;
                        SocketConnectivitySubchannelTransportLog.ErrorCheckingSocket(_logger, _subchannel.Id, _initialSocketAddress, ex);
                    }

                    if (closeSocket)
                    {
                        lock (Lock)
                        {
                            if (_disposed)
                            {
                                return;
                            }

                            if (_initialSocket == socket)
                            {
                                DisconnectUnsynchronized();
                            }
                        }
                        _subchannel.UpdateConnectivityState(ConnectivityState.Idle, new Status(StatusCode.Unavailable, "Lost connection to socket.", sendException));
                    }
                }
            }
            catch (Exception ex)
            {
                SocketConnectivitySubchannelTransportLog.ErrorSocketTimer(_logger, _subchannel.Id, ex);
            }
        }

        public async ValueTask<Stream> GetStreamAsync(BalancerAddress address, CancellationToken cancellationToken)
        {
            SocketConnectivitySubchannelTransportLog.CreatingStream(_logger, _subchannel.Id, address);

            Socket? socket = null;
            lock (Lock)
            {
                if (_initialSocket != null &&
                    _initialSocketAddress != null &&
                    Equals(_initialSocketAddress, address))
                {
                    socket = _initialSocket;
                    _initialSocket = null;
                    _initialSocketAddress = null;
                }
            }

            // Check to see if we've received anything on the connection; if we have, that's
            // either erroneous data (we shouldn't have received anything yet) or the connection
            // has been closed; either way, we can't use it.
            if (socket != null)
            {
                if (IsSocketInBadState(socket))
                {
                    socket.Dispose();
                    socket = null;
                }
            }

            if (socket == null)
            {
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(address.EndPoint, cancellationToken).ConfigureAwait(false);
            }

            var networkStream = new NetworkStream(socket, ownsSocket: true);

            // This stream wrapper intercepts dispose.
            var stream = new StreamWrapper(networkStream, OnStreamDisposed);

            lock (Lock)
            {
                _activeStreams.Add(new ActiveStream(address, socket, stream));
            }

            return stream;
        }

        private static bool IsSocketInBadState(Socket socket)
        {
            try
            {
                // Will return true if closed or there is pending data for some reason.
                return socket.Poll(0, SelectMode.SelectRead);
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                return false;
            }
        }

        private void OnStreamDisposed(Stream streamWrapper)
        {
            try
            {
                var disconnect = false;
                lock (Lock)
                {
                    for (var i = _activeStreams.Count - 1; i >= 0; i--)
                    {
                        var t = _activeStreams[i];
                        if (t.Stream == streamWrapper)
                        {
                            _activeStreams.RemoveAt(i);
                            SocketConnectivitySubchannelTransportLog.DisposingStream(_logger, _subchannel.Id, t.Address);

                            // If the last active streams is removed then there is no active connection.
                            disconnect = _activeStreams.Count == 0;

                            break;
                        }
                    }
                }

                if (disconnect)
                {
                    // What happens after disconnect depends if the load balancer requests a new connection.
                    // For example:
                    // - Pick first will go into an idle state.
                    // - Round-robin will reconnect to get back to a ready state.
                    Disconnect();
                }
            }
            catch (Exception ex)
            {
                // Don't throw error to Stream.Dispose() caller.
                SocketConnectivitySubchannelTransportLog.ErrorOnDisposingStream(_logger, _subchannel.Id, ex);
            }
        }

        public void Dispose()
        {
            lock (Lock)
            {
                if (_disposed)
                {
                    return;
                }

                SocketConnectivitySubchannelTransportLog.DisposingTransport(_logger, _subchannel.Id);

                DisconnectUnsynchronized();

                _socketConnectedTimer.Dispose();
                _disposed = true;
            }
        }

        public void OnRequestComplete(CompletionContext context)
        {
        }
    }

    internal static class SocketConnectivitySubchannelTransportLog
    {
        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _connectingSocket =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(1, "ConnectingSocket"), "Subchannel id '{SubchannelId}' connecting socket to {Address}.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _connectedSocket =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(2, "ConnectedSocket"), "Subchannel id '{SubchannelId}' connected to socket {Address}.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _errorConnectingSocket =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(3, "ErrorConnectingSocket"), "Subchannel id '{SubchannelId}' error connecting to socket {Address}.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _checkingSocket =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(4, "CheckingSocket"), "Subchannel id '{SubchannelId}' checking socket {Address}.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _errorCheckingSocket =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(5, "ErrorCheckingSocket"), "Subchannel id '{SubchannelId}' error checking socket {Address}.");

        private static readonly Action<ILogger, int, Exception?> _errorSocketTimer =
            LoggerMessage.Define<int>(LogLevel.Error, new EventId(6, "ErrorSocketTimer"), "Subchannel id '{SubchannelId}' unexpected error in check socket timer.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _creatingStream =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(7, "CreatingStream"), "Subchannel id '{SubchannelId}' creating stream for {Address}.");

        private static readonly Action<ILogger, int, BalancerAddress, Exception?> _disposingStream =
            LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(8, "DisposingStream"), "Subchannel id '{SubchannelId}' disposing stream for {Address}.");

        private static readonly Action<ILogger, int, Exception?> _disposingTransport =
            LoggerMessage.Define<int>(LogLevel.Trace, new EventId(9, "DisposingTransport"), "Subchannel id '{SubchannelId}' disposing transport.");

        private static readonly Action<ILogger, int, Exception> _errorOnDisposingStream =
            LoggerMessage.Define<int>(LogLevel.Error, new EventId(10, "ErrorOnDisposingStream"), "Subchannel id '{SubchannelId}' unexpected error when reacting to transport stream dispose.");

        public static void ConnectingSocket(ILogger logger, int subchannelId, BalancerAddress address)
        {
            _connectingSocket(logger, subchannelId, address, null);
        }

        public static void ConnectedSocket(ILogger logger, int subchannelId, BalancerAddress address)
        {
            _connectedSocket(logger, subchannelId, address, null);
        }

        public static void ErrorConnectingSocket(ILogger logger, int subchannelId, BalancerAddress address, Exception ex)
        {
            _errorConnectingSocket(logger, subchannelId, address, ex);
        }

        public static void CheckingSocket(ILogger logger, int subchannelId, BalancerAddress address)
        {
            _checkingSocket(logger, subchannelId, address, null);
        }

        public static void ErrorCheckingSocket(ILogger logger, int subchannelId, BalancerAddress address, Exception ex)
        {
            _errorCheckingSocket(logger, subchannelId, address, ex);
        }

        public static void ErrorSocketTimer(ILogger logger, int subchannelId, Exception ex)
        {
            _errorSocketTimer(logger, subchannelId, ex);
        }

        public static void CreatingStream(ILogger logger, int subchannelId, BalancerAddress address)
        {
            _creatingStream(logger, subchannelId, address, null);
        }

        public static void DisposingStream(ILogger logger, int subchannelId, BalancerAddress address)
        {
            _disposingStream(logger, subchannelId, address, null);
        }

        public static void DisposingTransport(ILogger logger, int subchannelId)
        {
            _disposingTransport(logger, subchannelId, null);
        }

        public static void ErrorOnDisposingStream(ILogger logger, int subchannelId, Exception ex)
        {
            _errorOnDisposingStream(logger, subchannelId, ex);
        }
    }
#endif
}
#endif
