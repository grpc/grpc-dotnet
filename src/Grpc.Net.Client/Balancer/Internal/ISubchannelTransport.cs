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
using System.Net;
using Grpc.Shared;

namespace Grpc.Net.Client.Balancer.Internal;

/// <summary>
/// An abstraction for subchannels to create a transport and connect to the server.
/// This abstraction allows the connection to be customized. Used in unit tests.
/// Might be made public in the future to support using load balancing with non-socket transports.
/// </summary>
internal interface ISubchannelTransport : IDisposable
{
    DnsEndPoint? CurrentEndPoint { get; }
    TimeSpan? ConnectTimeout { get; }
    TransportStatus TransportStatus { get; }

    ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken);
    ValueTask<ConnectResult> TryConnectAsync(ConnectContext context, int attempt);

    void Disconnect();
}

internal enum TransportStatus
{
    NotConnected,
    Passive,
    InitialSocket,
    ActiveStream
}

internal enum ConnectResult
{
    Success,
    Failure,
    Timeout
}

internal sealed class ConnectContext
{
    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _token;

    // This flag allows the transport to determine why the cancellation token was canceled.
    // - Explicit cancellation, e.g. the channel was disposed.
    // - Connection timeout, e.g. SocketsHttpHandler.ConnectTimeout was exceeded.
    public bool IsConnectCanceled { get; private set; }
    public bool Disposed { get; private set; }

    public CancellationToken CancellationToken => _token;

    public ConnectContext(TimeSpan connectTimeout)
    {
        _cts = new CancellationTokenSource(connectTimeout);

        // Take a copy of the token to avoid ObjectDisposedException when accessing _cts.Token after CTS is disposed.
        _token = _cts.Token;
    }

    public void CancelConnect()
    {
        // Check disposed because CTS.Cancel throws if the CTS is disposed.
        ObjectDisposedThrowHelper.ThrowIf(Disposed, typeof(ConnectContext));

        IsConnectCanceled = true;
        _cts.Cancel();
    }

    public void Dispose()
    {
        // Dispose the CTS because it could be created with an internal timer.
        _cts.Dispose();
        Disposed = true;
    }
}

internal interface ISubchannelTransportFactory
{
    ISubchannelTransport Create(Subchannel subchannel);
}
#endif
