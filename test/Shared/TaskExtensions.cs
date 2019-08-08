﻿#region Copyright notice and license

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
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Tests.Shared
{
    internal static class TaskExtensions
    {
        public static Task<T> DefaultTimeout<T>(this Task<T> task,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            return task.TimeoutAfter(TimeSpan.FromSeconds(5), filePath, lineNumber);
        }

        public static Task DefaultTimeout(this Task task,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            return task.TimeoutAfter(TimeSpan.FromSeconds(5), filePath, lineNumber);
        }

        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            // Don't create a timer if the task is already completed
            // or the debugger is attached
            if (task.IsCompleted || Debugger.IsAttached)
            {
                return await task;
            }

            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(timeout, cts.Token)))
            {
                cts.Cancel();
                return await task;
            }
            else
            {
                throw new TimeoutException(CreateMessage(timeout, filePath, lineNumber));
            }
        }

        public static async Task TimeoutAfter(this Task task, TimeSpan timeout,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            // Don't create a timer if the task is already completed
            // or the debugger is attached
            if (task.IsCompleted || Debugger.IsAttached)
            {
                await task;
                return;
            }

            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(timeout, cts.Token)))
            {
                cts.Cancel();
                await task;
            }
            else
            {
                throw new TimeoutException(CreateMessage(timeout, filePath, lineNumber));
            }
        }

        private static string CreateMessage(TimeSpan timeout, string? filePath, int lineNumber)
            => string.IsNullOrEmpty(filePath)
            ? $"The operation timed out after reaching the limit of {timeout.TotalMilliseconds}ms."
            : $"The operation at {filePath}:{lineNumber} timed out after reaching the limit of {timeout.TotalMilliseconds}ms.";

        public static IAsyncEnumerable<T> DefaultTimeout<T>(this IAsyncEnumerable<T> enumerable,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            return enumerable.TimeoutAfter(TimeSpan.FromSeconds(5), filePath, lineNumber);
        }

        public static IAsyncEnumerable<T> TimeoutAfter<T>(this IAsyncEnumerable<T> enumerable, TimeSpan timeout,
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = default)
        {
            return new TimeoutAsyncEnumerable<T>(enumerable, timeout, filePath, lineNumber);
        }

        private class TimeoutAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            private readonly IAsyncEnumerable<T> _inner;
            private readonly TimeSpan _timeout;
            private readonly string? _filePath;
            private readonly int _lineNumber;

            public TimeoutAsyncEnumerable(IAsyncEnumerable<T> inner, TimeSpan timeout, string? filePath, int lineNumber)
            {
                _inner = inner;
                _timeout = timeout;
                _filePath = filePath;
                _lineNumber = lineNumber;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new TimeoutAsyncEnumerator<T>(
                    _inner.GetAsyncEnumerator(cancellationToken),
                    _timeout,
                    _filePath,
                    _lineNumber);
            }
        }

        private class TimeoutAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            private readonly IAsyncEnumerator<T> _enumerator;
            private readonly TimeSpan _timeout;
            private readonly string? _filePath;
            private readonly int _lineNumber;

            public TimeoutAsyncEnumerator(IAsyncEnumerator<T> enumerator, TimeSpan timeout, string? filePath, int lineNumber)
            {
                _enumerator = enumerator;
                _timeout = timeout;
                _filePath = filePath;
                _lineNumber = lineNumber;
            }

            public T Current => _enumerator.Current;

            public ValueTask DisposeAsync()
            {
                return _enumerator.DisposeAsync();
            }

            public ValueTask<bool> MoveNextAsync()
            {
                return new ValueTask<bool>(_enumerator.MoveNextAsync().AsTask().TimeoutAfter(_timeout, _filePath, _lineNumber));
            }
        }
    }
}
