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

// Copied from https://github.com/grpc/grpc/tree/master/src/csharp/Grpc.IntegrationTesting
namespace QpsWorker.Infrastructure
{
    public interface IInterarrivalTimer
    {
        void WaitForNext();

        Task WaitForNextAsync();
    }

    /// <summary>
    /// Interarrival timer that doesn't wait at all.
    /// </summary>
    public class ClosedLoopInterarrivalTimer : IInterarrivalTimer
    {
        public ClosedLoopInterarrivalTimer()
        {
        }

        public void WaitForNext()
        {
            // NOP
        }

        public Task WaitForNextAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Interarrival timer that generates Poisson process load.
    /// </summary>
    public class PoissonInterarrivalTimer : IInterarrivalTimer
    {
        private readonly ExponentialDistribution _exponentialDistribution;
        private DateTime? _lastEventTime;

        public PoissonInterarrivalTimer(double offeredLoad)
        {
            _exponentialDistribution = new ExponentialDistribution(new Random(), offeredLoad);
            _lastEventTime = DateTime.UtcNow;
        }

        public void WaitForNext()
        {
            var waitDuration = GetNextWaitDuration();
            int millisTimeout = (int) Math.Round(waitDuration.TotalMilliseconds);
            if (millisTimeout > 0)
            {
                // TODO(jtattermusch): probably only works well for a relatively low interarrival rate
                Thread.Sleep(millisTimeout);
            }
        }

        public async Task WaitForNextAsync()
        {
            var waitDuration = GetNextWaitDuration();
            int millisTimeout = (int) Math.Round(waitDuration.TotalMilliseconds);
            if (millisTimeout > 0)
            {
                // TODO(jtattermusch): probably only works well for a relatively low interarrival rate
                await Task.Delay(millisTimeout);
            }
        }

        private TimeSpan GetNextWaitDuration()
        {
            if (!_lastEventTime.HasValue)
            {
                _lastEventTime = DateTime.Now;
            }

            var origLastEventTime = _lastEventTime.Value;
            _lastEventTime = origLastEventTime + TimeSpan.FromSeconds(_exponentialDistribution.Next());
            return _lastEventTime.Value - origLastEventTime;
        }

        /// <summary>
        /// Exp generator.
        /// </summary>
        private class ExponentialDistribution
        {
            private readonly Random _random;
            private readonly double _lambda;
            private readonly double _lambdaReciprocal;

            public ExponentialDistribution(Random random, double lambda)
            {
                _random = random;
                _lambda = lambda;
                _lambdaReciprocal = 1.0 / lambda;
            }

            public double Next()
            {
                double uniform = _random.NextDouble();
                // Use 1.0-uni above to avoid NaN if uni is 0
                return _lambdaReciprocal * (-Math.Log(1.0 - uniform));
            }
        }
    }
}
