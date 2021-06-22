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

#if HAVE_LOAD_BALANCING
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
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal
{
#if NET5_0_OR_GREATER
    internal class ActiveSubchannelTransport : ISubchannelTransport, IDisposable
    {
        private readonly SemaphoreSlim _connectionCreateLock;
        private readonly Subchannel _subchannel;
        private readonly TimeSpan _socketPingInterval;
        private int _lastEndPointIndex;

        internal readonly List<(DnsEndPoint EndPoint, Socket Socket, Stream? Stream)> _activeStreams;
        private readonly Timer _socketConnectedTimer;
        private Socket? _initialSocket;
        private DnsEndPoint? _initialSocketEndPoint;
        private bool _disposed;
        private DnsEndPoint? _currentEndPoint;

        public ActiveSubchannelTransport(Subchannel subchannel, TimeSpan socketPingInterval)
        {
            _connectionCreateLock = new SemaphoreSlim(1);
            _subchannel = subchannel;
            _socketPingInterval = socketPingInterval;
            _lastEndPointIndex = -1; // Start -1 so first attempt is at index 0

            _activeStreams = new List<(DnsEndPoint, Socket, Stream?)>();
            _socketConnectedTimer = new Timer(OnSocketConnected, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
                _lastEndPointIndex = -1; // Start -1 so first attempt is at index 0
                _socketConnectedTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
                _currentEndPoint = null;
            }
            _subchannel.UpdateConnectivityState(ConnectivityState.Idle);
        }

        public ValueTask<bool> TryConnectAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_subchannel._addresses.Count > 0);
            Debug.Assert(CurrentEndPoint == null);

            return TryConnectSocketAsync(cancellationToken);
        }

        private async ValueTask<bool> TryConnectSocketAsync(CancellationToken cancellationToken)
        {
            await _connectionCreateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Loop through endpoints and attempt to connect
                Exception? firstConnectionError = null;

                for (var i = 0; i < _subchannel._addresses.Count; i++)
                {
                    var currentIndex = (i + _lastEndPointIndex + 1) % _subchannel._addresses.Count;
                    var currentEndPoint = _subchannel._addresses[currentIndex];

                    Socket socket;

                    _subchannel.Logger.LogInformation("Creating socket: " + currentEndPoint);
                    socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    _subchannel.UpdateConnectivityState(ConnectivityState.Connecting);

                    try
                    {
                        _subchannel.Logger.LogInformation("Connecting: " + currentEndPoint);
                        await socket.ConnectAsync(currentEndPoint, cancellationToken).ConfigureAwait(false);
                        _subchannel.Logger.LogInformation("Connected: " + currentEndPoint);

                        lock (Lock)
                        {
                            _currentEndPoint = currentEndPoint;
                            _lastEndPointIndex = currentIndex;
                            _initialSocket = socket;
                            _initialSocketEndPoint = currentEndPoint;
                            _socketConnectedTimer.Change(_socketPingInterval, _socketPingInterval);
                        }

                        _subchannel.UpdateConnectivityState(ConnectivityState.Ready);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _subchannel.Logger.LogError("Connect error: " + currentEndPoint + " " + ex);

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
            finally
            {
                _connectionCreateLock.Release();
            }
        }

        private async void OnSocketConnected(object? state)
        {
            try
            {
                var socket = _initialSocket;
                if (socket != null)
                {
                    var closeSocket = false;
                    try
                    {
                        _subchannel.Logger.LogTrace("Checking socket: " + _initialSocketEndPoint);
                        await socket.SendAsync(Array.Empty<byte>(), SocketFlags.None).ConfigureAwait(false);
                        closeSocket = IsSocketInBadState(socket);
                    }
                    catch (Exception ex)
                    {
                        _subchannel.Logger.LogTrace(ex, "Error when pinging socket " + _initialSocketEndPoint);
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
                                _lastEndPointIndex = -1; // Start -1 so first attempt is at index 0
                            }
                        }
                        _subchannel.UpdateConnectivityState(ConnectivityState.Idle);
                    }
                }
            }
            catch (Exception ex)
            {
                _subchannel.Logger.LogError(ex, "Error when checking socket.");
            }
        }

        public async ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            _subchannel.Logger.LogInformation("GetStreamAsync: " + endPoint);

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
            var stream = new StreamWrapper(networkStream, OnStreamDisposed);

            lock (Lock)
            {
                _activeStreams.Add((endPoint, socket, stream));
                _subchannel.Logger.LogInformation("Transport stream created");
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
                        _subchannel.Logger.LogInformation("Disconnected: " + CurrentEndPoint);

                        disconnect = _activeStreams.Count == 0;

                        break;
                    }
                }
            }

            if (disconnect)
            {
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

        public void OnRequestComplete(CompleteContext context)
        {
        }
    }
#endif
}
#endif