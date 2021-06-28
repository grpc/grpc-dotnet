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

namespace Grpc.Net.Client.Tests.Infrastructure.Balancer
{
    internal class TestSubchannelTransportFactory : ISubchannelTransportFactory
    {
        private readonly Func<Subchannel, CancellationToken, Task<ConnectivityState>>? _onSubchannelTryConnect;

        public List<TestSubchannelTransport> Transports { get; } = new List<TestSubchannelTransport>();

        public TestSubchannelTransportFactory(Func<Subchannel, CancellationToken, Task<ConnectivityState>>? onSubchannelTryConnect = null)
        {
            _onSubchannelTryConnect = onSubchannelTryConnect;
        }

        public ISubchannelTransport Create(Subchannel subchannel)
        {
            Func<CancellationToken, Task<ConnectivityState>>? onTryConnect = null;
            if (_onSubchannelTryConnect != null)
            {
                onTryConnect = (c) => _onSubchannelTryConnect(subchannel, c);
            }

            var transport = new TestSubchannelTransport(subchannel, onTryConnect);
            Transports.Add(transport);

            return transport;
        }
    }
}
#endif
