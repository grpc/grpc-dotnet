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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Balancer
{
    public class LeastUsedBalancer : SubchannelsLoadBalancer
    {
        public LeastUsedBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory)
            : base(controller, loggerFactory)
        {
        }

        protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels)
        {
            return new LeastUsedPicker(readySubchannels);
        }
    }

    internal class LeastUsedPicker : SubchannelPicker
    {
        private static readonly BalancerAttributesKey<AtomicCounter> CounterKey = new BalancerAttributesKey<AtomicCounter>("ActiveRequestsCount");

        // Internal for testing
        internal readonly List<Subchannel> _subchannels;

        public LeastUsedPicker(IReadOnlyList<Subchannel> subchannels)
        {
            // Ensure all subchannels have an associated counter.
            foreach (var subchannel in subchannels)
            {
                if (!subchannel.Attributes.TryGetValue(CounterKey, out _))
                {
                    var counter = new AtomicCounter();
                    subchannel.Attributes.Set(CounterKey, counter);
                }
            }

            _subchannels = subchannels.ToList();
        }

        public override PickResult Pick(PickContext context)
        {
            Subchannel? leastUsedSubchannel = null;
            int? leastUsedCount = null;
            AtomicCounter? leastUsedCounter = null;

            foreach (var subchannel in _subchannels)
            {
                if (!subchannel.Attributes.TryGetValue(CounterKey, out var counter))
                {
                    throw new InvalidOperationException("All subchannels should have a counter.");
                }

                var currentCount = counter.Value;
                if (leastUsedSubchannel == null || leastUsedCount > currentCount)
                {
                    leastUsedSubchannel = subchannel;
                    leastUsedCount = currentCount;
                    leastUsedCounter = counter;
                }
            }

            Debug.Assert(leastUsedSubchannel != null);
            Debug.Assert(leastUsedCounter != null);

            leastUsedCounter.Increment();

            return PickResult.ForSubchannel(leastUsedSubchannel, c =>
            {
                leastUsedCounter.Decrement();
            });
        }

        public override string ToString()
        {
            return string.Join(", ", _subchannels.Select(s => s.ToString()));
        }

        private sealed class AtomicCounter
        {
            private int _value;

            /// <summary>
            /// Gets the current value of the counter.
            /// </summary>
            public int Value
            {
                get => Volatile.Read(ref _value);
                set => Volatile.Write(ref _value, value);
            }

            /// <summary>
            /// Atomically increments the counter value by 1.
            /// </summary>
            public int Increment()
            {
                return Interlocked.Increment(ref _value);
            }

            /// <summary>
            /// Atomically decrements the counter value by 1.
            /// </summary>
            public int Decrement()
            {
                return Interlocked.Decrement(ref _value);
            }

            /// <summary>
            /// Atomically resets the counter value to 0.
            /// </summary>
            public void Reset()
            {
                Interlocked.Exchange(ref _value, 0);
            }
        }
    }

    public class LeastUsedBalancerFactory : LoadBalancerFactory
    {
        public override string Name { get; } = "least_used";

        public override LoadBalancer Create(LoadBalancerOptions options)
        {
            return new LeastUsedBalancer(options.Controller, options.LoggerFactory);
        }
    }
}
#endif
