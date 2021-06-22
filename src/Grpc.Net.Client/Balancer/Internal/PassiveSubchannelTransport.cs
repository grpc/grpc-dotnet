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

namespace Grpc.Net.Client.Balancer.Internal
{
    internal class PassiveSubchannelTransport : ISubchannelTransport, IDisposable
    {
        private const int FailureThreshold = 5;

        private readonly Subchannel _subchannel;

        private int _lastEndPointIndex;
        private DnsEndPoint? _currentEndPoint;

        private int _failureCount;

        public PassiveSubchannelTransport(Subchannel subchannel)
        {
            _subchannel = subchannel;
            _lastEndPointIndex = -1; // Start -1 so first attempt is at index 0
        }

        public object Lock => _subchannel.Lock;
        public DnsEndPoint? CurrentEndPoint => _currentEndPoint;

        public void OnRequestComplete(CompletionContext context)
        {
            if (_currentEndPoint == null || !_currentEndPoint.Equals(context.Address))
            {
                return;
            }

            if (context.Error != null)
            {
                var passedThreshold = false;
                lock (Lock)
                {
                    _failureCount++;
                    if (_failureCount >= FailureThreshold)
                    {
                        passedThreshold = true;
                        _failureCount = 0;
                    }
                }

                if (passedThreshold)
                {
                    lock (Lock)
                    {
                        _currentEndPoint = null;
                    }
                    _subchannel.UpdateConnectivityState(ConnectivityState.Idle);
                }
            }
            else
            {
                lock (Lock)
                {
                    _failureCount = 0;
                }
            }
        }

        public void Disconnect()
        {
            lock (Lock)
            {
                _failureCount = 0;
                _currentEndPoint = null;
            }
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
            Debug.Assert(_subchannel._addresses.Count > 0);
            Debug.Assert(CurrentEndPoint == null);

            var currentIndex = (_lastEndPointIndex + 1) % _subchannel._addresses.Count;
            var currentEndPoint = _subchannel._addresses[currentIndex];

            _subchannel.UpdateConnectivityState(ConnectivityState.Connecting);
            lock (Lock)
            {
                _currentEndPoint = currentEndPoint;
                _lastEndPointIndex = currentIndex;
            }
            _subchannel.UpdateConnectivityState(ConnectivityState.Ready);

#if !NETSTANDARD2_0
            return new ValueTask<bool>(true);
#else
            return Task.FromResult(true);
#endif
        }

        public void Dispose()
        {
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
