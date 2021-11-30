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

namespace Grpc.Net.Client.Tests.Infrastructure.Balancer
{
    internal class TestSubchannelTransport : ISubchannelTransport
    {
        private ConnectivityState _state = ConnectivityState.Idle;
        private TaskCompletionSource<object?> _connectTcs;
        private readonly Func<CancellationToken, Task<ConnectivityState>>? _onTryConnect;

        public Subchannel Subchannel { get; }

        public BalancerAddress? CurrentAddress { get; private set; }

        public Task TryConnectTask => _connectTcs.Task;

        public TestSubchannelTransport(Subchannel subchannel, Func<CancellationToken, Task<ConnectivityState>>? onTryConnect)
        {
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

        public ValueTask<Stream> GetStreamAsync(BalancerAddress address, CancellationToken cancellationToken)
        {
            return new ValueTask<Stream>(new MemoryStream());
        }

        public void OnRequestComplete(CompletionContext context)
        {
        }

        public void Disconnect()
        {
            CurrentAddress = null;
            Subchannel.UpdateConnectivityState(ConnectivityState.Idle, "Disconnected.");
        }

        public async
#if !NET472
            ValueTask<bool>
#else
            Task<bool>
#endif
            TryConnectAsync(CancellationToken cancellationToken)
        {
            var newState = await (_onTryConnect?.Invoke(cancellationToken) ?? Task.FromResult(ConnectivityState.Ready));

            CurrentAddress = Subchannel._addresses[0];
            Subchannel.UpdateConnectivityState(newState, Status.DefaultSuccess);

            _connectTcs.TrySetResult(null);

            return newState == ConnectivityState.Ready;
        }
    }
}
#endif
