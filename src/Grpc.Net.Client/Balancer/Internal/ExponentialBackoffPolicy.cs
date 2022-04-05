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
using Grpc.Net.Client.Internal;

namespace Grpc.Net.Client.Balancer.Internal
{
    internal sealed class ExponentialBackoffPolicy : IBackoffPolicy
    {
        internal const double Multiplier = 1.6;
        internal const double Jitter = 0.2;

        private readonly IRandomGenerator _randomGenerator;
        private readonly long _maxBackoffTicks;
        private long _nextBackoffTicks;

        public ExponentialBackoffPolicy(
            IRandomGenerator randomGenerator,
            long initialBackoffTicks,
            long maxBackoffTicks)
        {
            Debug.Assert(initialBackoffTicks > 0);
            Debug.Assert(maxBackoffTicks <= int.MaxValue);

            _randomGenerator = randomGenerator;
            _nextBackoffTicks = initialBackoffTicks;
            _maxBackoffTicks = maxBackoffTicks;
        }

        public long GetNextBackoffTicks()
        {
            var currentBackoffTicks = _nextBackoffTicks;
            _nextBackoffTicks = Math.Min((long)Math.Round(currentBackoffTicks * Multiplier), _maxBackoffTicks);

            currentBackoffTicks += UniformRandom(-Jitter * currentBackoffTicks, Jitter * currentBackoffTicks);
            return currentBackoffTicks;
        }

        private long UniformRandom(double low, double high)
        {
            Debug.Assert(high >= low);

            var mag = high - low;
            return (long)(_randomGenerator.NextDouble() * mag + low);
        }
    }

    internal sealed class ExponentialBackoffPolicyFactory : IBackoffPolicyFactory
    {
        private readonly GrpcChannel _channel;

        public ExponentialBackoffPolicyFactory(GrpcChannel channel)
        {
            _channel = channel;
        }

        public IBackoffPolicy Create()
        {
            // Limit ticks to Int32.MaxValue. Task.Delay can't use larger values,
            // and larger values mean we need to worry about overflows.
            return new ExponentialBackoffPolicy(
                _channel.RandomGenerator,
                Math.Min(_channel.InitialReconnectBackoff.Ticks, int.MaxValue),
                Math.Min(_channel.MaxReconnectBackoff?.Ticks ?? long.MaxValue, int.MaxValue));
        }
    }
}
#endif
