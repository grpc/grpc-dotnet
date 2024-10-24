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
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal;

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
internal sealed class SocketConnectivitySubchannelTransport : ISubchannelTransport, IDisposable
{
    private const int MaximumInitialSocketDataSize = 1024 * 16;
    internal static readonly TimeSpan SocketPingInterval = TimeSpan.FromSeconds(5);
    internal readonly record struct ActiveStream(DnsEndPoint EndPoint, Socket Socket, Stream? Stream);

    private readonly ILogger _logger;
    private readonly Subchannel _subchannel;
    private readonly TimeSpan _socketPingInterval;
    private readonly TimeSpan _socketIdleTimeout;
    private readonly Func<Socket, DnsEndPoint, CancellationToken, ValueTask> _socketConnect;
    private readonly List<ActiveStream> _activeStreams;
    private readonly Timer _socketConnectedTimer;

    private int _lastEndPointIndex;
    internal Socket? _initialSocket;
    private DnsEndPoint? _initialSocketEndPoint;
    private List<ReadOnlyMemory<byte>>? _initialSocketData;
    private DateTime? _initialSocketCreatedTime;
    private bool _disposed;
    private DnsEndPoint? _currentEndPoint;

    public SocketConnectivitySubchannelTransport(
        Subchannel subchannel,
        TimeSpan socketPingInterval,
        TimeSpan? connectTimeout,
        TimeSpan socketIdleTimeout,
        ILoggerFactory loggerFactory,
        Func<Socket, DnsEndPoint, CancellationToken, ValueTask>? socketConnect)
    {
        _logger = loggerFactory.CreateLogger(typeof(SocketConnectivitySubchannelTransport));
        _subchannel = subchannel;
        _socketPingInterval = socketPingInterval;
        ConnectTimeout = connectTimeout;
        _socketIdleTimeout = socketIdleTimeout;
        _socketConnect = socketConnect ?? OnConnect;
        _activeStreams = new List<ActiveStream>();
        _socketConnectedTimer = NonCapturingTimer.Create(OnCheckSocketConnection, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private object Lock => _subchannel.Lock;
    public DnsEndPoint? CurrentEndPoint => _currentEndPoint;
    public TimeSpan? ConnectTimeout { get; }
    public TransportStatus TransportStatus
    {
        get
        {
            lock (Lock)
            {
                if (_initialSocket != null)
                {
                    return TransportStatus.InitialSocket;
                }
                if (_activeStreams.Count > 0)
                {
                    return TransportStatus.ActiveStream;
                }
                return TransportStatus.NotConnected;
            }
        }
    }

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
        _initialSocketEndPoint = null;
        _initialSocketData = null;
        _initialSocketCreatedTime = null;
        _lastEndPointIndex = 0;
        _currentEndPoint = null;
    }

    public async ValueTask<ConnectResult> TryConnectAsync(ConnectContext context, int attempt)
    {
        Debug.Assert(CurrentEndPoint == null);

        // Addresses could change while connecting. Make a copy of the subchannel's addresses.
        var addresses = _subchannel.GetAddresses();

        // Loop through endpoints and attempt to connect.
        Exception? firstConnectionError = null;

        for (var i = 0; i < addresses.Count; i++)
        {
            var currentIndex = (i + _lastEndPointIndex) % addresses.Count;
            var currentEndPoint = addresses[currentIndex].EndPoint;

            Socket socket;

            socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _subchannel.UpdateConnectivityState(ConnectivityState.Connecting, "Connecting to socket.");

            try
            {
                SocketConnectivitySubchannelTransportLog.ConnectingSocket(_logger, _subchannel.Id, currentEndPoint);
                await _socketConnect(socket, currentEndPoint, context.CancellationToken).ConfigureAwait(false);
                SocketConnectivitySubchannelTransportLog.ConnectedSocket(_logger, _subchannel.Id, currentEndPoint);

                lock (Lock)
                {
                    _currentEndPoint = currentEndPoint;
                    _lastEndPointIndex = currentIndex;
                    _initialSocket = socket;
                    _initialSocketEndPoint = currentEndPoint;
                    _initialSocketData = null;
                    _initialSocketCreatedTime = DateTime.UtcNow;

                    // Schedule ping. Don't set a periodic interval to avoid any chance of timer causing the target method to run multiple times in paralle.
                    // This could happen because of execution delays (e.g. hitting a debugger breakpoint).
                    // Instead, the socket timer target method reschedules the next run after it has finished.
                    _socketConnectedTimer.Change(_socketPingInterval, Timeout.InfiniteTimeSpan);
                }

                _subchannel.UpdateConnectivityState(ConnectivityState.Ready, "Successfully connected to socket.");
                return ConnectResult.Success;
            }
            catch (Exception ex)
            {
                // Socket is recreated every connect attempt. Explicitly dispose failed socket before next attempt.
                socket.Dispose();

                SocketConnectivitySubchannelTransportLog.ErrorConnectingSocket(_logger, _subchannel.Id, currentEndPoint, ex);

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

    private static int CalculateInitialSocketDataLength(List<ReadOnlyMemory<byte>>? initialSocketData)
    {
        if (initialSocketData == null)
        {
            return 0;
        }

        var length = 0;
        foreach (var data in initialSocketData)
        {
            length += data.Length;
        }
        return length;
    }

    private void OnCheckSocketConnection(object? state)
    {
        try
        {
            Socket? socket;
            DnsEndPoint? socketEndpoint;
            var closeSocket = false;
            Exception? checkException = null;
            DateTime? socketCreatedTime;

            lock (Lock)
            {
                socket = _initialSocket;
                socketEndpoint = _initialSocketEndPoint;
                socketCreatedTime = _initialSocketCreatedTime;

                if (socket != null)
                {
                    CompatibilityHelpers.Assert(socketEndpoint != null);
                    CompatibilityHelpers.Assert(socketCreatedTime != null);

                    closeSocket = ShouldCloseSocket(socket, socketEndpoint, ref _initialSocketData, out checkException);
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
                        CompatibilityHelpers.Assert(socketEndpoint != null);
                        CompatibilityHelpers.Assert(socketCreatedTime != null);

                        SocketConnectivitySubchannelTransportLog.ClosingUnusableSocket(_logger, _subchannel.Id, socketEndpoint, DateTime.UtcNow - socketCreatedTime.Value);
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
            // Double-check that there is still an initial socket before scheduling the next ping.
            // Parallel code execution could cause the socket to be removed, e.g. a gRPC call runs GetStreamAsync.
            if (_initialSocket != null && !_disposed)
            {
                // Schedule next ping.
                _socketConnectedTimer.Change(_socketPingInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public async ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        SocketConnectivitySubchannelTransportLog.CreatingStream(_logger, _subchannel.Id, endPoint);

        Socket? socket = null;
        DnsEndPoint? socketEndPoint = null;
        List<ReadOnlyMemory<byte>>? socketData = null;
        DateTime? socketCreatedTime = null;
        lock (Lock)
        {
            if (_initialSocket != null)
            {
                var socketEndPointMatch = Equals(_initialSocketEndPoint, endPoint);

                socket = _initialSocket;
                socketEndPoint = _initialSocketEndPoint;
                socketData = _initialSocketData;
                socketCreatedTime = _initialSocketCreatedTime;
                _initialSocket = null;
                _initialSocketEndPoint = null;
                _initialSocketData = null;
                _initialSocketCreatedTime = null;

                // Double check the endpoint matches the socket endpoint and only use socket on match.
                // Not sure if this is possible in practice, but better safe than sorry.
                if (!socketEndPointMatch)
                {
                    socket.Dispose();
                    socket = null;
                }

                _socketConnectedTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        if (socket != null)
        {
            Debug.Assert(socketCreatedTime != null);

            var closeSocket = false;

            if (_socketIdleTimeout != Timeout.InfiniteTimeSpan && DateTime.UtcNow > socketCreatedTime.Value.Add(_socketIdleTimeout))
            {
                SocketConnectivitySubchannelTransportLog.ClosingSocketFromIdleTimeoutOnCreateStream(_logger, _subchannel.Id, endPoint, _socketIdleTimeout);
                closeSocket = true;
            }
            else if (ShouldCloseSocket(socket, endPoint, ref socketData, out _))
            {
                SocketConnectivitySubchannelTransportLog.ClosingUnusableSocket(_logger, _subchannel.Id, endPoint, DateTime.UtcNow - socketCreatedTime.Value);
                closeSocket = true;
            }

            if (closeSocket)
            {
                socket.Dispose();
                socket = null;
                socketData = null;
            }
        }

        if (socket == null)
        {
            SocketConnectivitySubchannelTransportLog.ConnectingOnCreateStream(_logger, _subchannel.Id, endPoint);

            socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        }

        var networkStream = new NetworkStream(socket, ownsSocket: true);

        // This stream wrapper intercepts dispose.
        var stream = new StreamWrapper(networkStream, OnStreamDisposed, socketData);

        lock (Lock)
        {
            _activeStreams.Add(new ActiveStream(endPoint, socket, stream));
            SocketConnectivitySubchannelTransportLog.StreamCreated(_logger, _subchannel.Id, endPoint, CalculateInitialSocketDataLength(socketData), _activeStreams.Count);
        }

        return stream;
    }

    /// <summary>
    /// Checks whether the socket is healthy. May read available data into the passed in buffer.
    /// Returns true if the socket should be closed.
    /// </summary>
    private bool ShouldCloseSocket(Socket socket, DnsEndPoint socketEndPoint, ref List<ReadOnlyMemory<byte>>? socketData, out Exception? checkException)
    {
        checkException = null;

        try
        {
            SocketConnectivitySubchannelTransportLog.CheckingSocket(_logger, _subchannel.Id, socketEndPoint);

            // Poll socket to check if it can be read from. Unfortunately this requires reading pending data.
            // The server might send data, e.g. HTTP/2 SETTINGS frame, so we need to read and cache it.
            //
            // Available data needs to be read now because the only way to determine whether the connection is
            // closed is to get the results of polling after available data is received.
            // For example, the server may have sent an HTTP/2 SETTINGS or GOAWAY frame.
            // We need to cache whatever we read so it isn't dropped.
            do
            {
                if (PollSocket(socket, socketEndPoint))
                {
                    // Polling socket reported an unhealthy state.
                    return true;
                }

                var available = socket.Available;
                if (available > 0)
                {
                    var serverDataAvailable = CalculateInitialSocketDataLength(socketData) + available;
                    if (serverDataAvailable > MaximumInitialSocketDataSize)
                    {
                        // Data sent to the client before a connection is started shouldn't be large.
                        // Put a maximum limit on the buffer size to prevent an unexpected scenario from consuming too much memory.
                        throw new InvalidOperationException($"The server sent {serverDataAvailable} bytes to the client before a connection was established. Maximum allowed data exceeded.");
                    }

                    SocketConnectivitySubchannelTransportLog.SocketReceivingAvailable(_logger, _subchannel.Id, socketEndPoint, available);

                    // Data is already available so this won't block.
                    var buffer = new byte[available];
                    var readCount = socket.Receive(buffer);

                    socketData ??= new List<ReadOnlyMemory<byte>>();
                    socketData.Add(buffer.AsMemory(0, readCount));
                }
                else
                {
                    // There is no more available data to read and the socket is healthy.
                    return false;
                }
            }
            while (true);
        }
        catch (Exception ex)
        {
            checkException = ex;
            SocketConnectivitySubchannelTransportLog.ErrorCheckingSocket(_logger, _subchannel.Id, socketEndPoint, ex);
            return true;
        }
    }

    /// <summary>
    /// Poll the socket to check for health and available data.
    /// Shouldn't be used by itself as data needs to be consumed to accurately report the socket health.
    /// <see cref="ShouldCloseSocket"/> handles consuming data and getting the socket health.
    /// </summary>
    private bool PollSocket(Socket socket, DnsEndPoint endPoint)
    {
        // From https://github.com/dotnet/runtime/blob/3195fbbd82fdb7f132d6698591ba6489ad6dd8cf/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs#L158-L168
        try
        {
            // Will return true if closed or there is pending data for some reason.
            var result = socket.Poll(microSeconds: 0, SelectMode.SelectRead);
            if (result && socket.Available > 0)
            {
                result = !socket.Connected;
            }
            if (result)
            {
                SocketConnectivitySubchannelTransportLog.SocketPollBadState(_logger, _subchannel.Id, endPoint);
            }
            return result;
        }
        catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
        {
            // Poll can throw when used on a closed socket.
            SocketConnectivitySubchannelTransportLog.ErrorPollingSocket(_logger, _subchannel.Id, endPoint, ex);
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
                        SocketConnectivitySubchannelTransportLog.DisposingStream(_logger, _subchannel.Id, t.EndPoint, _activeStreams.Count);

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

internal static partial class SocketConnectivitySubchannelTransportLog
{
    [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "ConnectingSocket", Message = "Subchannel id '{SubchannelId}' connecting socket to {EndPoint}.")]
    public static partial void ConnectingSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2, EventName = "ConnectedSocket", Message = "Subchannel id '{SubchannelId}' connected to socket {EndPoint}.")]
    public static partial void ConnectedSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 3, EventName = "ErrorConnectingSocket", Message = "Subchannel id '{SubchannelId}' error connecting to socket {EndPoint}.")]
    public static partial void ErrorConnectingSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 4, EventName = "CheckingSocket", Message = "Subchannel id '{SubchannelId}' checking socket {EndPoint}.")]
    public static partial void CheckingSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 5, EventName = "ErrorCheckingSocket", Message = "Subchannel id '{SubchannelId}' error checking socket {EndPoint}.")]
    public static partial void ErrorCheckingSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, EventId = 6, EventName = "ErrorSocketTimer", Message = "Subchannel id '{SubchannelId}' unexpected error in check socket timer.")]
    public static partial void ErrorSocketTimer(ILogger logger, string subchannelId, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 7, EventName = "CreatingStream", Message = "Subchannel id '{SubchannelId}' creating stream for {EndPoint}.")]
    public static partial void CreatingStream(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 8, EventName = "DisposingStream", Message = "Subchannel id '{SubchannelId}' disposing stream for {EndPoint}. Transport has {ActiveStreams} active streams.")]
    public static partial void DisposingStream(ILogger logger, string subchannelId, DnsEndPoint endPoint, int activeStreams);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 9, EventName = "DisposingTransport", Message = "Subchannel id '{SubchannelId}' disposing transport.")]
    public static partial void DisposingTransport(ILogger logger, string subchannelId);

    [LoggerMessage(Level = LogLevel.Error, EventId = 10, EventName = "ErrorOnDisposingStream", Message = "Subchannel id '{SubchannelId}' unexpected error when reacting to transport stream dispose.")]
    public static partial void ErrorOnDisposingStream(ILogger logger, string subchannelId, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 11, EventName = "ConnectingOnCreateStream", Message = "Subchannel id '{SubchannelId}' doesn't have a connected socket available. Connecting new stream socket for {EndPoint}.")]
    public static partial void ConnectingOnCreateStream(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 12, EventName = "StreamCreated", Message = "Subchannel id '{SubchannelId}' created stream for {EndPoint} with {BufferedBytes} buffered bytes. Transport has {ActiveStreams} active streams.")]
    public static partial void StreamCreated(ILogger logger, string subchannelId, DnsEndPoint endPoint, int bufferedBytes, int activeStreams);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 13, EventName = "ErrorPollingSocket", Message = "Subchannel id '{SubchannelId}' error checking socket {EndPoint}.")]
    public static partial void ErrorPollingSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 14, EventName = "SocketPollBadState", Message = "Subchannel id '{SubchannelId}' socket {EndPoint} is in a bad state and can't be used.")]
    public static partial void SocketPollBadState(ILogger logger, string subchannelId, DnsEndPoint endPoint);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 15, EventName = "SocketReceivingAvailable", Message = "Subchannel id '{SubchannelId}' socket {EndPoint} is receiving {ReadBytesAvailableCount} available bytes.")]
    public static partial void SocketReceivingAvailable(ILogger logger, string subchannelId, DnsEndPoint endPoint, int readBytesAvailableCount);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 16, EventName = "ClosingUnusableSocket", Message = "Subchannel id '{SubchannelId}' socket {EndPoint} is being closed because it can't be used. Socket lifetime of {SocketLifetime}. The socket either can't receive data or it has received unexpected data.")]
    public static partial void ClosingUnusableSocket(ILogger logger, string subchannelId, DnsEndPoint endPoint, TimeSpan socketLifetime);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 17, EventName = "ClosingSocketFromIdleTimeoutOnCreateStream", Message = "Subchannel id '{SubchannelId}' socket {EndPoint} is being closed because it exceeds the idle timeout of {SocketIdleTimeout}.")]
    public static partial void ClosingSocketFromIdleTimeoutOnCreateStream(ILogger logger, string subchannelId, DnsEndPoint endPoint, TimeSpan socketIdleTimeout);
}
#endif
