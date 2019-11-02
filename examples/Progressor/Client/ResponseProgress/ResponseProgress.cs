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
using Grpc.Core;

namespace Client.ResponseProgress
{
    public static class GrpcProgress
    {
        public static ResponseProgress<TResult, TProgress> Create<TResult, TProgress>(
            IAsyncStreamReader<IProgressMessage<TResult, TProgress>> streamReader,
            IProgress<TProgress>? progress = null)
        {
            return new ResponseProgress<TResult, TProgress>(streamReader, progress);
        }
    }

    public class ResponseProgress<TResult, TProgress> : Progress<TProgress>
    {
        private readonly IAsyncStreamReader<IProgressMessage<TResult, TProgress>> _streamReader;
        private readonly IProgress<TProgress>? _progress;
        private readonly Task<TResult> _resultTask;

        public ResponseProgress(IAsyncStreamReader<IProgressMessage<TResult, TProgress>> streamReader, IProgress<TProgress>? progress = null)
        {
            _streamReader = streamReader;
            _progress = progress;

            // Start reading from the stream in the background, updating IProgress with values from the server.
            // When the result is returned set it into the task complete source.
            _resultTask = Task.Run<TResult>(async () =>
            {
                await foreach (var item in _streamReader.ReadAllAsync())
                {
                    if (item.IsProgress)
                    {
                        _progress?.Report(item.Progress);
                        OnReport(item.Progress);
                    }
                    if (item.IsResult)
                    {
                        return item.Result;
                    }
                }

                throw new Exception("Call completed without a result.");
            });
        }

        public TaskAwaiter<TResult> GetAwaiter()
        {
            return _resultTask.GetAwaiter();
        }
    }
}
