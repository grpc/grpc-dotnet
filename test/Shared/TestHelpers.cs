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
using System.IO;
using System.Threading.Tasks;

namespace Grpc.Tests.Shared
{
    public static class TestHelpers
    {
        public static string ResolvePath(string relativePath)
        {
            var resolvedPath = Path.Combine(Path.GetDirectoryName(typeof(TestHelpers).Assembly.Location)!, relativePath);

            return resolvedPath;
        }

        public static async Task AssertIsTrueRetryAsync(Func<bool> assert, string message)
        {
            const int Retrys = 10;

            for (int i = 0; i < Retrys; i++)
            {
                if (i > 0)
                {
                    await Task.Delay((i + 1) * 10);
                }

                if (assert())
                {
                    return;
                }
            }

            throw new Exception($"Assert failed after {Retrys} retries: {message}");
        }

        public static async Task RunParallel(int count, Func<int, Task> action)
        {
            var actionTasks = new Task[count];
            for (int i = 0; i < actionTasks.Length; i++)
            {
                actionTasks[i] = action(i);
            }

            await Task.WhenAll(actionTasks);
        }
    }
}
