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

using System.Diagnostics;
using Grpc.Core.Utils;
using Grpc.Testing;

// Copied from https://github.com/grpc/grpc/tree/master/src/csharp/Grpc.IntegrationTesting
namespace QpsWorker.Infrastructure
{
    /// <summary>
    /// Basic implementation of histogram based on grpc/support/histogram.h.
    /// </summary>
    public class Histogram
    {
        private readonly object _myLock = new object();
        private readonly double _multiplier;
        private readonly double _oneOnLogMultiplier;
        private readonly double _maxPossible;
        private readonly uint[] _buckets;

        private int _count;
        private double _sum;
        private double _sumOfSquares;
        private double _min;
        private double _max;

        public Histogram(double resolution, double maxPossible)
        {
            GrpcPreconditions.CheckArgument(resolution > 0);
            GrpcPreconditions.CheckArgument(maxPossible > 0);
            _maxPossible = maxPossible;
            _multiplier = 1.0 + resolution;
            _oneOnLogMultiplier = 1.0 / Math.Log(1.0 + resolution);
            _buckets = new uint[FindBucket(maxPossible) + 1];

            ResetUnsafe();
        }

        public void AddObservation(double value)
        {
            lock (_myLock)
            {
                AddObservationUnsafe(value);
            }
        }

        /// <summary>
        /// Gets snapshot of stats and optionally resets the histogram.
        /// </summary>
        public HistogramData GetSnapshot(bool reset = false)
        {
            lock (_myLock)
            {
                var histogramData = new HistogramData();
                GetSnapshotUnsafe(histogramData, reset);
                return histogramData;
            }
        }

        /// <summary>
        /// Merges snapshot of stats into <c>mergeTo</c> and optionally resets the histogram.
        /// </summary>
        public void GetSnapshot(HistogramData mergeTo, bool reset)
        {
            lock (_myLock)
            {
                GetSnapshotUnsafe(mergeTo, reset);
            }
        }

        /// <summary>
        /// Finds bucket index to which given observation should go.
        /// </summary>
        private int FindBucket(double value)
        {
            value = Math.Max(value, 1.0);
            value = Math.Min(value, _maxPossible);
            return (int)(Math.Log(value) * _oneOnLogMultiplier);
        }

        private void AddObservationUnsafe(double value)
        {
            _count++;
            _sum += value;
            _sumOfSquares += value * value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);

            _buckets[FindBucket(value)]++;
        }

        private void GetSnapshotUnsafe(HistogramData mergeTo, bool reset)
        {
            GrpcPreconditions.CheckArgument(mergeTo.Bucket.Count == 0 || mergeTo.Bucket.Count == _buckets.Length);
            if (mergeTo.Count == 0)
            {
                mergeTo.MinSeen = _min;
                mergeTo.MaxSeen = _max;
            }
            else
            {
                mergeTo.MinSeen = Math.Min(mergeTo.MinSeen, _min);
                mergeTo.MaxSeen = Math.Max(mergeTo.MaxSeen, _max);
            }
            mergeTo.Count += _count;
            mergeTo.Sum += _sum;
            mergeTo.SumOfSquares += _sumOfSquares;

            if (mergeTo.Bucket.Count == 0)
            {
                mergeTo.Bucket.AddRange(_buckets);
            }
            else
            {
                for (int i = 0; i < _buckets.Length; i++)
                {
                    mergeTo.Bucket[i] += _buckets[i];
                }
            }

            if (reset)
            {
                ResetUnsafe();
            }
        }

        private void ResetUnsafe()
        {
            _count = 0;
            _sum = 0;
            _sumOfSquares = 0;
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = 0;
            }
        }
    }
}
