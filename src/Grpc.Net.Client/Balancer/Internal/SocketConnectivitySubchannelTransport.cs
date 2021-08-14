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
        private readonly ILogger _logger;
        private readonly Subchannel _subchannel;
        private readonly TimeSpan _socketPingInterval;
        internal readonly List<(DnsEndPoint EndPoint, Socket Socket, Stream? Stream)> _activeStreams;
        private readonly Timer _socketConnectedTimer;

        private int _lastEndPointIndex;
        private Socket? _initialSocket;
        private DnsEndPoint? _initialSocketEndPoint;
        private bool _disposed;
        private DnsEndPoint? _currentEndPoint;

        public SocketConnectivitySubchannelTransport(Subchannel subchannel, TimeSpan socketPingInterval, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SocketConnectivitySubchannelTransport>();
            _subchannel = subchannel;
            _socketPingInterval = socketPingInterval;
            _activeStreams = new List<(DnsEndPoint, Socket, Stream?)>();
            _socketConnectedTimer = new Timer(OnCheckSocketConnection, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public object Lock => _subchannel.Lock;
        public DnsEndPoint? CurrentEndPoint => _currentEndPoint;
        public bool HasStream { get; }

        public void Disconnect()
        {
            lock (Lock)
            {
                _initialSocket?.Dispose();
                _initialSocket = null;
                _initialSocketEndPoint = null;
                _lastEndPointIndex = 0;
                _socketConnectedTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
                _currentEndPoint = null;
            }
            _subchannel.UpdateConnectivityState(ConnectivityState.Idle, "Disconnected.");
        }

        public async ValueTask<bool> TryConnectAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(CurrentEndPoint == null);

            // Addresses could change while connecting. Make a copy of the subchannel's addresses.
            var addresses = _subchannel.GetAddresses();

            // Loop through endpoints and attempt to connect.
            Exception? firstConnectionError = null;

            for (var i = 0; i < addresses.Count; i++)
            {
                var currentIndex = (i + _lastEndPointIndex) % addresses.Count;
                var currentEndPoint = addresses[currentIndex];

                Socket socket;

                socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                _subchannel.UpdateConnectivityState(ConnectivityState.Connecting, "Connecting to socket.");

                try
                {
                    SocketConnectivitySubchannelTransportLog.ConnectingSocket(_logger, currentEndPoint);
                    await socket.ConnectAsync(currentEndPoint, cancellationToken).ConfigureAwait(false);
                    SocketConnectivitySubchannelTransportLog.ConnectedSocket(_logger, currentEndPoint);

                    lock (Lock)
                    {
                        _currentEndPoint = currentEndPoint;
                        _lastEndPointIndex = currentIndex;
                        _initialSocket = socket;
                        _initialSocketEndPoint = currentEndPoint;
                        _socketConnectedTimer.Change(_socketPingInterval, _socketPingInterval);
                    }

                    _subchannel.UpdateConnectivityState(ConnectivityState.Ready, "Successfully connected to socket.");
                    return true;
                }
                catch (Exception ex)
                {
                    SocketConnectivitySubchannelTransportLog.ErrorConnectingSocket(_logger, currentEndPoint, ex);

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
                    CompatibilityHelpers.Assert(_initialSocketEndPoint != null);

                    var closeSocket = false;
                    Exception? sendException = null;
                    try
                    {
                        // Check the socket is still valid by doing a zero byte send.
                        SocketConnectivitySubchannelTransportLog.CheckingSocket(_logger, _initialSocketEndPoint);
                        await socket.SendAsync(Array.Empty<byte>(), SocketFlags.None).ConfigureAwait(false);

                        // Also poll socket to check if it can be read from.
                        closeSocket = IsSocketInBadState(socket);
                    }
                    catch (Exception ex)
                    {
                        closeSocket = true;
                        sendException = ex;
                        SocketConnectivitySubchannelTransportLog.ErrorCheckingSocket(_logger, _initialSocketEndPoint, ex);
                    }

                    if (closeSocket)
                    {
                        lock (Lock)
                        {
                            if (_initialSocket == socket)
                            {
                                _initialSocket.Dispose();
                                _initialSocket = null;
                                _initialSocketEndPoint = null;
                                _currentEndPoint = null;
                                _lastEndPointIndex = 0;
                            }
                        }
                        _subchannel.UpdateConnectivityState(ConnectivityState.Idle, new Status(StatusCode.Unavailable, "Lost connection to socket.", sendException));
                    }
                }
            }
            catch (Exception ex)
            {
                SocketConnectivitySubchannelTransportLog.ErrorSocketTimer(_logger, ex);
            }
        }

        public async ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            SocketConnectivitySubchannelTransportLog.CreatingStream(_logger, endPoint);

            Socket? socket = null;
            lock (Lock)
            {
                if (_initialSocket != null &&
                    _initialSocketEndPoint != null &&
                    Equals(_initialSocketEndPoint, endPoint))
                {
                    socket = _initialSocket;
                    _initialSocket = null;
                    _initialSocketEndPoint = null;
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
                await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            }

            var networkStream = new NetworkStream(socket, ownsSocket: true);

            // This stream wrapper intercepts dispose.
            var stream = new StreamWrapper(networkStream, OnStreamDisposed);

            lock (Lock)
            {
                _activeStreams.Add((endPoint, socket, stream));
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
            var disconnect = false;
            lock (Lock)
            {
                for (var i = _activeStreams.Count - 1; i >= 0; i--)
                {
                    var t = _activeStreams[i];
                    if (t.Stream == streamWrapper)
                    {
                        _activeStreams.RemoveAt(i);
                        SocketConnectivitySubchannelTransportLog.DisposingStream(_logger, t.EndPoint);

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

        public void Dispose()
        {
            lock (Lock)
            {
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
        private static readonly Action<ILogger, DnsEndPoint, Exception?> _connectingSocket =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Trace, new EventId(1, "ConnectingSocket"), "Connecting socket to '{Address}'.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _connectedSocket =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Debug, new EventId(1, "ConnectedSocket"), "Connected to socket '{Address}'.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _errorConnectingSocket =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Error, new EventId(1, "ErrorConnectingSocket"), "Error connecting to socket '{Address}'.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _checkingSocket =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Trace, new EventId(1, "CheckingSocket"), "Checking socket '{Address}'.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _errorCheckingSocket =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Error, new EventId(1, "ErrorCheckingSocket"), "Error checking socket '{Address}'.");

        private static readonly Action<ILogger, Exception?> _errorSocketTimer =
            LoggerMessage.Define(LogLevel.Error, new EventId(1, "ErrorSocketTimer"), "Unexpected error in check socket timer.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _creatingStream =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Trace, new EventId(1, "CreatingStream"), "Creating stream for '{Address}'.");

        private static readonly Action<ILogger, DnsEndPoint, Exception?> _disposingStream =
            LoggerMessage.Define<DnsEndPoint>(LogLevel.Trace, new EventId(1, "DisposingStream"), "Disposing stream for '{Address}'.");

        public static void ConnectingSocket(ILogger logger, DnsEndPoint address)
        {
            _connectingSocket(logger, address, null);
        }

        public static void ConnectedSocket(ILogger logger, DnsEndPoint address)
        {
            _connectedSocket(logger, address, null);
        }

        public static void ErrorConnectingSocket(ILogger logger, DnsEndPoint address, Exception ex)
        {
            _errorConnectingSocket(logger, address, ex);
        }

        public static void CheckingSocket(ILogger logger, DnsEndPoint address)
        {
            _checkingSocket(logger, address, null);
        }

        public static void ErrorCheckingSocket(ILogger logger, DnsEndPoint address, Exception ex)
        {
            _errorCheckingSocket(logger, address, ex);
        }

        public static void ErrorSocketTimer(ILogger logger, Exception ex)
        {
            _errorSocketTimer(logger, ex);
        }

        public static void CreatingStream(ILogger logger, DnsEndPoint address)
        {
            _creatingStream(logger, address, null);
        }

        public static void DisposingStream(ILogger logger, DnsEndPoint address)
        {
            _disposingStream(logger, address, null);
        }
    }
#endif
}
#endif
