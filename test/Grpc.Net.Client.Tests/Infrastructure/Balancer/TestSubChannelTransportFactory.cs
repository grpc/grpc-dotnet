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
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;

namespace Grpc.Net.Client.Tests.Infrastructure.Balancer;

internal record TryConnectResult(ConnectivityState ConnectivityState, ConnectResult? ConnectResult = null);

internal class TestSubchannelTransportFactory : ISubchannelTransportFactory
{
    private readonly Func<Subchannel, int, CancellationToken, Task<TryConnectResult>>? _onSubchannelTryConnect;

    public List<TestSubchannelTransport> Transports { get; } = new List<TestSubchannelTransport>();
    public TimeSpan? ConnectTimeout { get; set; }

    public TestSubchannelTransportFactory()
    {
    }

    private TestSubchannelTransportFactory(Func<Subchannel, int, CancellationToken, Task<TryConnectResult>>? onSubchannelTryConnect = null)
    {
        _onSubchannelTryConnect = onSubchannelTryConnect;
    }

    public static TestSubchannelTransportFactory Create(Func<Subchannel, int, CancellationToken, Task<TryConnectResult>> onSubchannelTryConnect)
    {
        return new TestSubchannelTransportFactory(onSubchannelTryConnect);
    }

    public static TestSubchannelTransportFactory Create(Func<Subchannel, CancellationToken, Task<TryConnectResult>> onSubchannelTryConnect)
    {
        return Create((subchannel, attempt, cancellationToken) => onSubchannelTryConnect(subchannel, cancellationToken));
    }

    public ISubchannelTransport Create(Subchannel subchannel)
    {
        Func<int, CancellationToken, Task<TryConnectResult>>? onTryConnect = null;
        if (_onSubchannelTryConnect != null)
        {
            onTryConnect = (attempt, c) => _onSubchannelTryConnect(subchannel, attempt, c);
        }

        var transport = new TestSubchannelTransport(this, subchannel, onTryConnect);
        Transports.Add(transport);

        return transport;
    }
}
#endif
