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

using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer
{
    [TestFixture]
    public class ExponentialBackoffPolicyTests
    {
        [Test]
        public void InitialBackoffTicks_FirstCall_ReturnsConfiguredValue()
        {
            var policy = new ExponentialBackoffPolicy(
                new TestRandomGenerator(),
                initialBackoffTicks: 10,
                maxBackoffTicks: 2000);

            Assert.AreEqual(10, policy.GetNextBackoffTicks());
            Assert.AreEqual(16, policy.GetNextBackoffTicks());
        }

        [Test]
        public void MaxBackoffTicks_MultipleCalls_ReachesLimit()
        {
            var policy = new ExponentialBackoffPolicy(
                new TestRandomGenerator(),
                initialBackoffTicks: 10,
                maxBackoffTicks: 2000);

            for (var i = 0; i < 50; i++)
            {
                if (policy.GetNextBackoffTicks() == 2000)
                {
                    break;
                }
            }

            Assert.AreEqual(2000, policy.GetNextBackoffTicks());
        }

        [Test]
        public void MaxBackoffTicks_Int32MaxValueWithJitter_ReturnsUpToInt32PlusMaxJitter()
        {
            const long MaximumWithJitter = (long)(int.MaxValue + int.MaxValue * ExponentialBackoffPolicy.Jitter);

            var policy = new ExponentialBackoffPolicy(
                new TestRandomGenerator() { DoubleResult = 1 },
                initialBackoffTicks: 1,
                maxBackoffTicks: int.MaxValue);

            for (var i = 0; i < 1000; i++)
            {
                var ticks = policy.GetNextBackoffTicks();

                Assert.Greater(ticks, 0);
                Assert.LessOrEqual(ticks, MaximumWithJitter);
            }

            Assert.AreEqual(policy.GetNextBackoffTicks(), MaximumWithJitter);
        }

        private class TestRandomGenerator : IRandomGenerator
        {
            public double DoubleResult { get; set; } = 0.5;

            public int Next(int minValue, int maxValue)
            {
                return 0;
            }

            public double NextDouble()
            {
                return DoubleResult;
            }
        }
    }
}

#endif
