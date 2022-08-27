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

using Microsoft.Extensions.Logging;

namespace Grpc.Tests.Shared
{
    public static class TestHelpers
    {
        public static Task WaitForCancellationAsync(this CancellationToken cancellationToken)
        {
            // Server abort doesn't happen inline.
            // Wait for the token to be triggered to confirm abort has happened.
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.SetResult(null));
            return tcs.Task;
        }

        public static string ResolvePath(string relativePath)
        {
            var resolvedPath = Path.Combine(Path.GetDirectoryName(typeof(TestHelpers).Assembly.Location)!, relativePath);

            return resolvedPath;
        }

        public static Task AssertIsTrueRetryAsync(Func<bool> assert, string message, ILogger? logger = null)
        {
            return AssertIsTrueRetryAsync(() => Task.FromResult(assert()), message, logger);
        }

        public static async Task AssertIsTrueRetryAsync(Func<Task<bool>> assert, string message, ILogger? logger = null)
        {
            const int Retries = 10;

            logger?.LogInformation("Start: " + message);

            for (var i = 0; i < Retries; i++)
            {
                if (i > 0)
                {
                    await Task.Delay((i + 1) * (i + 1) * 10);
                }

                if (await assert())
                {
                    logger?.LogInformation("End: " + message);
                    return;
                }
            }

            throw new Exception($"Assert failed after {Retries} retries: {message}");
        }

        public static async Task RunParallel(int count, Func<int, Task> action)
        {
            var actionTasks = new Task[count];
            for (var i = 0; i < actionTasks.Length; i++)
            {
                actionTasks[i] = action(i);
            }

            await Task.WhenAll(actionTasks);
        }
    }
}
