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

namespace Grpc.Net.Client.Balancer.Internal;

/// <summary>
/// Subchannel transport used when SocketsHttpHandler isn't configured.
/// This transport will only be used when there is one address.
/// It isn't able to correctly determine connectivity state.
/// </summary>
internal sealed class PassiveSubchannelTransport : ISubchannelTransport, IDisposable
{
    private readonly Subchannel _subchannel;
    private DnsEndPoint? _currentEndPoint;

    public PassiveSubchannelTransport(Subchannel subchannel)
    {
        _subchannel = subchannel;
    }

    public DnsEndPoint? CurrentEndPoint => _currentEndPoint;
    public TimeSpan? ConnectTimeout { get; }
    public TransportStatus TransportStatus => TransportStatus.Passive;

    public void Disconnect()
    {
        _currentEndPoint = null;
        _subchannel.UpdateConnectivityState(ConnectivityState.Idle, "Disconnected.");
    }

    public ValueTask<ConnectResult> TryConnectAsync(ConnectContext context, int attempt)
    {
        Debug.Assert(_subchannel._addresses.Count == 1);
        Debug.Assert(CurrentEndPoint == null);

        var currentAddress = _subchannel._addresses[0];

        _subchannel.UpdateConnectivityState(ConnectivityState.Connecting, "Passively connecting.");
        _currentEndPoint = currentAddress.EndPoint;
        _subchannel.UpdateConnectivityState(ConnectivityState.Ready, "Passively connected.");

        return new ValueTask<ConnectResult>(ConnectResult.Success);
    }

    public void Dispose()
    {
        _currentEndPoint = null;
    }

    public ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
#endif
