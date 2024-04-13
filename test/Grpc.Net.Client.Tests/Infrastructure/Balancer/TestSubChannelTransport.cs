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
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;

namespace Grpc.Net.Client.Tests.Infrastructure.Balancer;

internal class TestSubchannelTransport : ISubchannelTransport
{
    private ConnectivityState _state = ConnectivityState.Idle;

    private readonly TaskCompletionSource<object?> _connectTcs;
    private readonly TestSubchannelTransportFactory _factory;
    private readonly Func<int, CancellationToken, Task<TryConnectResult>>? _onTryConnect;

    public Subchannel Subchannel { get; }

    public DnsEndPoint? CurrentEndPoint { get; private set; }
    public TimeSpan? ConnectTimeout => _factory.ConnectTimeout;
    public TransportStatus TransportStatus => TransportStatus.Passive;

    public Task TryConnectTask => _connectTcs.Task;

    public TestSubchannelTransport(TestSubchannelTransportFactory factory, Subchannel subchannel, Func<int, CancellationToken, Task<TryConnectResult>>? onTryConnect)
    {
        _factory = factory;
        Subchannel = subchannel;
        _onTryConnect = onTryConnect;
        _connectTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void UpdateState(ConnectivityState state, Status? status = null)
    {
        _state = state;
        Subchannel.UpdateConnectivityState(_state, status ?? Status.DefaultSuccess);
    }

    public void Dispose()
    {
    }

    public ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        return new ValueTask<Stream>(new MemoryStream());
    }

    public void Disconnect()
    {
        CurrentEndPoint = null;
        Subchannel.UpdateConnectivityState(ConnectivityState.Idle, "Disconnected.");
    }

    public async
#if !NET462
        ValueTask<ConnectResult>
#else
        Task<ConnectResult>
#endif
        TryConnectAsync(ConnectContext context, int attempt)
    {
        var (newState, connectResult) = await (_onTryConnect?.Invoke(attempt, context.CancellationToken) ?? Task.FromResult(new TryConnectResult(ConnectivityState.Ready)));

        CurrentEndPoint = Subchannel._addresses[0].EndPoint;
        var newStatus = newState == ConnectivityState.TransientFailure ? new Status(StatusCode.Internal, "") : Status.DefaultSuccess;
        Subchannel.UpdateConnectivityState(newState, newStatus);

        _connectTcs.TrySetResult(null);

        if (connectResult is null)
        {
            return newState == ConnectivityState.Ready ? ConnectResult.Success : ConnectResult.Failure;
        }

        return connectResult.Value;
    }
}
#endif
