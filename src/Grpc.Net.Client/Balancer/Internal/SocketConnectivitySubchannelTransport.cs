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
using System.Buffers;
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

namespace Grpc.Net.Client.Balancer.Internal;

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
    internal static readonly TimeSpan SocketPingInterval = TimeSpan.FromSeconds(5);
    internal readonly record struct ActiveStream(BalancerAddress Address, Socket Socket, Stream? Stream);

    private readonly ILogger _logger;
    private readonly Subchannel _subchannel;
    private readonly TimeSpan _socketPingInterval;
    private readonly Func<Socket, DnsEndPoint, CancellationToken, ValueTask> _socketConnect;
    private readonly List<ActiveStream> _activeStreams;
    private readonly Timer _socketConnectedTimer;

    private int _lastEndPointIndex;
    internal Socket? _initialSocket;
    private BalancerAddress? _initialSocketAddress;
    private int _initialSocketDirtyBytesReadCount;
    private bool _disposed;
    private BalancerAddress? _currentAddress;

    public SocketConnectivitySubchannelTransport(
        Subchannel subchannel,
        TimeSpan socketPingInterval,
        TimeSpan? connectTimeout,
        ILoggerFactory loggerFactory,
        Func<Socket, DnsEndPoint, CancellationToken, ValueTask>? socketConnect)
    {
        _logger = loggerFactory.CreateLogger<SocketConnectivitySubchannelTransport>();
        _subchannel = subchannel;
        _socketPingInterval = socketPingInterval;
        ConnectTimeout = connectTimeout;
        _socketConnect = socketConnect ?? OnConnect;
        _activeStreams = new List<ActiveStream>();
        _socketConnectedTimer = NonCapturingTimer.Create(OnCheckSocketConnection, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private object Lock => _subchannel.Lock;
    public BalancerAddress? CurrentAddress => _currentAddress;
    public TimeSpan? ConnectTimeout { get; }

    // For testing. Take a copy under lock for thread-safety.
    internal IReadOnlyList<ActiveStream> GetActiveStreams()
    {
        lock (Lock)
        {
            return _activeStreams.ToList();
        }
    }

    private static ValueTask OnConnect(Socket socket, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        return socket.ConnectAsync(endpoint, cancellationToken);
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
            _socketConnectedTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

    public async ValueTask<ConnectResult> TryConnectAsync(ConnectContext context)
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
                await _socketConnect(socket, currentAddress.EndPoint, context.CancellationToken).ConfigureAwait(false);
                SocketConnectivitySubchannelTransportLog.ConnectedSocket(_logger, _subchannel.Id, currentAddress);

                lock (Lock)
                {
                    _currentAddress = currentAddress;
                    _lastEndPointIndex = currentIndex;
                    _initialSocket = socket;
                    _initialSocketAddress = currentAddress;

                    // Schedule ping. Don't set a periodic interval to avoid any chance of timer running the logic multiple
                    // times because of execution delays (e.g. hitting a debugger breakpoint).
                    _socketConnectedTimer.Change(_socketPingInterval, Timeout.InfiniteTimeSpan);
                }

                _subchannel.UpdateConnectivityState(ConnectivityState.Ready, "Successfully connected to socket.");
                return ConnectResult.Success;
            }
            catch (Exception ex)
            {
                SocketConnectivitySubchannelTransportLog.ErrorConnectingSocket(_logger, _subchannel.Id, currentAddress, ex);

                if (firstConnectionError == null)
                {
                    firstConnectionError = ex;
                }

                // Stop trying to connect to addresses on cancellation.
                if (context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        var result = ConnectResult.Failure;

        // Check if cancellation happened because of timeout.
        if (firstConnectionError is OperationCanceledException oce &&
            oce.CancellationToken == context.CancellationToken &&
            !context.IsConnectCanceled)
        {
            firstConnectionError = new TimeoutException("A connection could not be established within the configured ConnectTimeout.", firstConnectionError);
            result = ConnectResult.Timeout;
        }

        // All connections failed
        _subchannel.UpdateConnectivityState(
            ConnectivityState.TransientFailure,
            new Status(StatusCode.Unavailable, "Error connecting to subchannel.", firstConnectionError));
        lock (Lock)
        {
            if (!_disposed)
            {
                _socketConnectedTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
        return result;
    }

    private void OnCheckSocketConnection(object? state)
    {
        try
        {
            Socket? socket;
            BalancerAddress? socketAddress;
            var closeSocket = false;
            Exception? checkException = null;

            lock (Lock)
            {
                socket = _initialSocket;
                socketAddress = _initialSocketAddress;

                if (socket != null)
                {
                    CompatibilityHelpers.Assert(socketAddress != null);

                    try
                    {
                        SocketConnectivitySubchannelTransportLog.CheckingSocket(_logger, _subchannel.Id, socketAddress);

                        // Poll socket to check if it can be read from.
                        // We don't care that there is available data to be be read at this point. If there is available data
                        // then the socket can't be used by SocketsHttpHandler, but the socket can get recreated when it is eventually used.
                        //
                        // Available data needs to be read because the only way to determine whether the connection is closed is to
                        // get the results of polling after available data is received.
                        //
                        // If a socket with data is closed immediately then that might happen continuously.
                        // Chill, and be happy that the socket can be read from.
                        do
                        {
                            closeSocket = IsSocketInBadState(socket, socketAddress, allowAvailableReadBytes: true);
                            var available = socket.Available;
                            if (available > 0)
                            {
                                SocketConnectivitySubchannelTransportLog.SocketReceivingAvailable(_logger, _subchannel.Id, socketAddress, available);

                                var buffer = ArrayPool<byte>.Shared.Rent(minimumLength: available);
                                try
                                {
                                    // Data is already available so this won't block.
                                    var readCount = socket.Receive(buffer);
                                    _initialSocketDirtyBytesReadCount += readCount;
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(buffer);
                                }
                            }
                        }
                        while (socket.Available > 0);
                    }
                    catch (Exception ex)
                    {
                        closeSocket = true;
                        checkException = ex;
                        SocketConnectivitySubchannelTransportLog.ErrorCheckingSocket(_logger, _subchannel.Id, socketAddress, ex);
                    }
                }
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
                _subchannel.UpdateConnectivityState(ConnectivityState.Idle, new Status(StatusCode.Unavailable, "Lost connection to socket.", checkException));
            }
        }
        catch (Exception ex)
        {
            SocketConnectivitySubchannelTransportLog.ErrorSocketTimer(_logger, _subchannel.Id, ex);
        }

        lock (Lock)
        {
            if (!_disposed)
            {
                // Schedule next ping.
                _socketConnectedTimer.Change(_socketPingInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public async ValueTask<Stream> GetStreamAsync(BalancerAddress address, CancellationToken cancellationToken)
    {
        SocketConnectivitySubchannelTransportLog.CreatingStream(_logger, _subchannel.Id, address);

        Socket? socket = null;
        BalancerAddress? socketAddress = null;
        lock (Lock)
        {
            if (_initialSocket != null)
            {
                var socketAddressMatch = Equals(_initialSocketAddress, address);

                socket = _initialSocket;
                socketAddress = _initialSocketAddress;
                _initialSocket = null;
                _initialSocketAddress = null;

                // Double check the address matches the socket address and only use socket on match.
                // Not sure if this is possible in practice, but better safe than sorry.
                if (!socketAddressMatch)
                {
                    socket.Dispose();
                    socket = null;
                }
            }
        }

        // Check to see if we've received anything on the connection; if we have, that's
        // either erroneous data (we shouldn't have received anything yet) or the connection
        // has been closed; either way, we can't use it.
        if (socket != null)
        {
            if (_initialSocketDirtyBytesReadCount > 0 || IsSocketInBadState(socket, address, allowAvailableReadBytes: false))
            {
                SocketConnectivitySubchannelTransportLog.ClosingUnusableSocket(_logger, _subchannel.Id, address);

                socket.Dispose();
                socket = null;
            }
        }

        if (socket == null)
        {
            SocketConnectivitySubchannelTransportLog.ConnectingOnCreateStream(_logger, _subchannel.Id, address);

            socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(address.EndPoint, cancellationToken).ConfigureAwait(false);
        }

        var networkStream = new NetworkStream(socket, ownsSocket: true);

        // This stream wrapper intercepts dispose.
        var stream = new StreamWrapper(networkStream, OnStreamDisposed);

        lock (Lock)
        {
            _activeStreams.Add(new ActiveStream(address, socket, stream));
            SocketConnectivitySubchannelTransportLog.StreamCreated(_logger, _subchannel.Id, address, _activeStreams.Count);
        }

        return stream;
    }

    private bool IsSocketInBadState(Socket socket, BalancerAddress address, bool allowAvailableReadBytes)
    {
        // Check to see if we've received anything on the connection; if we have, that's either erroneous data (we shouldn't have received anything yet)
        // or the connection has been closed; either way, we can't use it.
        //
        // From https://github.com/dotnet/runtime/blob/3195fbbd82fdb7f132d6698591ba6489ad6dd8cf/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L158-L168
        // This implementation adds a flag to allow read bytes.
        try
        {
            // Will return true if closed or there is pending data for some reason.
            var result = socket.Poll(microSeconds: 0, SelectMode.SelectRead);
            _logger.LogInformation($"Poll result: {result}, Available: {socket.Available}, Connected: {socket.Connected}");
            if (result && allowAvailableReadBytes && socket.Available > 0)
            {
                result = !socket.Connected;
            }
            if (result)
            {
                SocketConnectivitySubchannelTransportLog.SocketPollBadState(_logger, _subchannel.Id, address, socket.Available);
            }
            return result;
        }
        catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
        {
            // Poll can throw when used on a closed socket.
            SocketConnectivitySubchannelTransportLog.ErrorPollingSocket(_logger, _subchannel.Id, address, ex);
            return true;
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
                        SocketConnectivitySubchannelTransportLog.DisposingStream(_logger, _subchannel.Id, t.Address, _activeStreams.Count);

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
}

internal static class SocketConnectivitySubchannelTransportLog
{
    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _connectingSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(1, "ConnectingSocket"), "Subchannel id '{SubchannelId}' connecting socket to {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _connectedSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(2, "ConnectedSocket"), "Subchannel id '{SubchannelId}' connected to socket {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception> _errorConnectingSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(3, "ErrorConnectingSocket"), "Subchannel id '{SubchannelId}' error connecting to socket {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _checkingSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(4, "CheckingSocket"), "Subchannel id '{SubchannelId}' checking socket {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception> _errorCheckingSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(5, "ErrorCheckingSocket"), "Subchannel id '{SubchannelId}' error checking socket {Address}.");

    private static readonly Action<ILogger, int, Exception> _errorSocketTimer =
        LoggerMessage.Define<int>(LogLevel.Error, new EventId(6, "ErrorSocketTimer"), "Subchannel id '{SubchannelId}' unexpected error in check socket timer.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _creatingStream =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(7, "CreatingStream"), "Subchannel id '{SubchannelId}' creating stream for {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, int, Exception?> _disposingStream =
        LoggerMessage.Define<int, BalancerAddress, int>(LogLevel.Trace, new EventId(8, "DisposingStream"), "Subchannel id '{SubchannelId}' disposing stream for {Address}. Transport has {ActiveStreams} active streams.");

    private static readonly Action<ILogger, int, Exception?> _disposingTransport =
        LoggerMessage.Define<int>(LogLevel.Trace, new EventId(9, "DisposingTransport"), "Subchannel id '{SubchannelId}' disposing transport.");

    private static readonly Action<ILogger, int, Exception> _errorOnDisposingStream =
        LoggerMessage.Define<int>(LogLevel.Error, new EventId(10, "ErrorOnDisposingStream"), "Subchannel id '{SubchannelId}' unexpected error when reacting to transport stream dispose.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _connectingOnCreateStream =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Trace, new EventId(11, "ConnectingOnCreateStream"), "Subchannel id '{SubchannelId}' doesn't have a connected socket available. Connecting new stream socket for {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, int, Exception?> _streamCreated =
        LoggerMessage.Define<int, BalancerAddress, int>(LogLevel.Trace, new EventId(12, "StreamCreated"), "Subchannel id '{SubchannelId}' created stream for {Address}. Transport has {ActiveStreams} active streams.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception> _errorPollingSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(13, "ErrorPollingSocket"), "Subchannel id '{SubchannelId}' error checking socket {Address}.");

    private static readonly Action<ILogger, int, BalancerAddress, int, Exception?> _socketPollBadState =
        LoggerMessage.Define<int, BalancerAddress, int>(LogLevel.Debug, new EventId(14, "SocketPollBadState"), "Subchannel id '{SubchannelId}' socket {Address} is in a bad state and can't be used. Either it is not connected, or there is unexpected pending read data. Read bytes available count: {ReadBytesAvailableCount}");

    private static readonly Action<ILogger, int, BalancerAddress, int, Exception?> _socketReceivingAvailable =
        LoggerMessage.Define<int, BalancerAddress, int>(LogLevel.Trace, new EventId(15, "SocketReceivingAvailable"), "Subchannel id '{SubchannelId}' socket {Address} is receiving {ReadBytesAvailableCount} available bytes.");

    private static readonly Action<ILogger, int, BalancerAddress, Exception?> _closingUnusableSocket =
        LoggerMessage.Define<int, BalancerAddress>(LogLevel.Debug, new EventId(16, "ClosingUnusableSocket"), "Subchannel id '{SubchannelId}' socket {Address} is being closed because it can't be used. The socket either can't receive data or it has received unexpected data.");

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

    public static void DisposingStream(ILogger logger, int subchannelId, BalancerAddress address, int activeStreams)
    {
        _disposingStream(logger, subchannelId, address, activeStreams, null);
    }

    public static void DisposingTransport(ILogger logger, int subchannelId)
    {
        _disposingTransport(logger, subchannelId, null);
    }

    public static void ErrorOnDisposingStream(ILogger logger, int subchannelId, Exception ex)
    {
        _errorOnDisposingStream(logger, subchannelId, ex);
    }

    public static void ConnectingOnCreateStream(ILogger logger, int subchannelId, BalancerAddress address)
    {
        _connectingOnCreateStream(logger, subchannelId, address, null);
    }

    public static void StreamCreated(ILogger logger, int subchannelId, BalancerAddress address, int activeStreams)
    {
        _streamCreated(logger, subchannelId, address, activeStreams, null);
    }

    public static void ErrorPollingSocket(ILogger logger, int subchannelId, BalancerAddress address, Exception ex)
    {
        _errorPollingSocket(logger, subchannelId, address, ex);
    }

    public static void SocketPollBadState(ILogger logger, int subchannelId, BalancerAddress address, int readBytesAvailableCount)
    {
        _socketPollBadState(logger, subchannelId, address, readBytesAvailableCount, null);
    }

    public static void SocketReceivingAvailable(ILogger logger, int subchannelId, BalancerAddress address, int readBytesAvailableCount)
    {
        _socketReceivingAvailable(logger, subchannelId, address, readBytesAvailableCount, null);
    }

    public static void ClosingUnusableSocket(ILogger logger, int subchannelId, BalancerAddress address)
    {
        _closingUnusableSocket(logger, subchannelId, address, null);
    }

    // 
}
#endif
#endif
