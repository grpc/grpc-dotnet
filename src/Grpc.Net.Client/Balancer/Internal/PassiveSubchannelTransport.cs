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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.Net.Client.Balancer.Internal
{
    /// <summary>
    /// Subchannel transport used when SocketsHttpHandler isn't configured.
    /// This transport will only be used when there is one address.
    /// It isn't able to correctly determine connectivity state.
    /// </summary>
    internal class PassiveSubchannelTransport : ISubchannelTransport, IDisposable
    {
        private readonly Subchannel _subchannel;
        private DnsEndPoint? _currentEndPoint;

        public PassiveSubchannelTransport(Subchannel subchannel)
        {
            _subchannel = subchannel;
        }

        public DnsEndPoint? CurrentEndPoint => _currentEndPoint;

        public void OnRequestComplete(CompletionContext context)
        {
        }

        public void Disconnect()
        {
            _currentEndPoint = null;
            _subchannel.UpdateConnectivityState(ConnectivityState.Idle);
        }

        public
#if !NETSTANDARD2_0
            ValueTask<bool>
#else
            Task<bool>
#endif
            TryConnectAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_subchannel._addresses.Count == 1);
            Debug.Assert(CurrentEndPoint == null);

            var currentEndPoint = _subchannel._addresses[0];

            _subchannel.UpdateConnectivityState(ConnectivityState.Connecting);
            _currentEndPoint = currentEndPoint;
            _subchannel.UpdateConnectivityState(ConnectivityState.Ready);

#if !NETSTANDARD2_0
            return new ValueTask<bool>(true);
#else
            return Task.FromResult(true);
#endif
        }

        public void Dispose()
        {
            _currentEndPoint = null;
        }

#if NET5_0_OR_GREATER
        public ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
#endif
    }
}
#endif
