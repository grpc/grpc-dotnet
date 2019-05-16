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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Tests.Shared
{
    public class SyncPoint
    {
        private readonly TaskCompletionSource<object?> _atSyncPoint = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _continueFromSyncPoint = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Waits for the code-under-test to reach <see cref="WaitToContinue"/>.
        /// </summary>
        /// <returns></returns>
        public Task WaitForSyncPoint() => _atSyncPoint.Task;

        /// <summary>
        /// Cancel waiting for the code-under-test to reach <see cref="WaitToContinue"/>.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        public void CancelWaitForSyncPoint(CancellationToken cancellationToken) => _atSyncPoint.TrySetCanceled(cancellationToken);

        /// <summary>
        /// Releases the code-under-test to continue past where it waited for <see cref="WaitToContinue"/>.
        /// </summary>
        public void Continue() => _continueFromSyncPoint.TrySetResult(null);

        /// <summary>
        /// Used by the code-under-test to wait for the test code to sync up.
        /// </summary>
        /// <remarks>
        /// This code will unblock <see cref="WaitForSyncPoint"/> and then block waiting for <see cref="Continue"/> to be called.
        /// </remarks>
        /// <returns></returns>
        public Task WaitToContinue()
        {
            _atSyncPoint.TrySetResult(null);
            return _continueFromSyncPoint.Task;
        }

        public static Func<Task> Create(out SyncPoint syncPoint)
        {
            var handler = Create(1, out var syncPoints);
            syncPoint = syncPoints[0];
            return handler;
        }

        /// <summary>
        /// Creates a re-entrant function that waits for sync points in sequence.
        /// </summary>
        /// <param name="count">The number of sync points to expect</param>
        /// <param name="syncPoints">The <see cref="SyncPoint"/> objects that can be used to coordinate the sync point</param>
        /// <returns></returns>
        public static Func<Task> Create(int count, out SyncPoint[] syncPoints)
        {
            // Need to use a local so the closure can capture it. You can't use out vars in a closure.
            var localSyncPoints = new SyncPoint[count];
            for (var i = 0; i < count; i += 1)
            {
                localSyncPoints[i] = new SyncPoint();
            }

            syncPoints = localSyncPoints;

            var counter = 0;
            return () =>
            {
                if (counter >= localSyncPoints.Length)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    var syncPoint = localSyncPoints[counter];

                    counter += 1;
                    return syncPoint.WaitToContinue();
                }
            };
        }
    }
}
