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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    internal static class ForceAsyncTaskExtensions
    {
        /// <summary>
        /// Returns an awaitable/awaiter that will ensure the continuation is executed
        /// asynchronously on the thread pool, even if the task is already completed
        /// by the time the await occurs.  Effectively, it is equivalent to awaiting
        /// with ConfigureAwait(false) and then queuing the continuation with Task.Run,
        /// but it avoids the extra hop if the continuation already executed asynchronously.
        /// </summary>
        public static ForceAsyncAwaiter ForceAsync(this Task task)
        {
            return new ForceAsyncAwaiter(task);
        }

        public static ForceAsyncAwaiter<T> ForceAsync<T>(this Task<T> task)
        {
            return new ForceAsyncAwaiter<T>(task);
        }
    }

    internal readonly struct ForceAsyncAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task _task;

        internal ForceAsyncAwaiter(Task task) { _task = task; }

        public ForceAsyncAwaiter GetAwaiter() { return this; }

        // The purpose of this type is to always force a continuation
        public bool IsCompleted => false;

        public void GetResult() { _task.GetAwaiter().GetResult(); }

        public void OnCompleted(Action action)
        {
            _task.ConfigureAwait(false).GetAwaiter().OnCompleted(action);
        }

        public void UnsafeOnCompleted(Action action)
        {
            _task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(action);
        }
    }

    internal readonly struct ForceAsyncAwaiter<T> : ICriticalNotifyCompletion
    {
        private readonly Task<T> _task;

        internal ForceAsyncAwaiter(Task<T> task) { _task = task; }

        public ForceAsyncAwaiter<T> GetAwaiter() { return this; }

        // The purpose of this type is to always force a continuation
        public bool IsCompleted => false;

        public T GetResult() { return _task.GetAwaiter().GetResult(); }

        public void OnCompleted(Action action)
        {
            _task.ConfigureAwait(false).GetAwaiter().OnCompleted(action);
        }

        public void UnsafeOnCompleted(Action action)
        {
            _task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(action);
        }
    }
}
