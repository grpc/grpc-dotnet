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
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace Grpc.Tests.Shared
{
    public class TestResponseBodyFeature : IHttpResponseBodyFeature
    {
        public TestResponseBodyFeature(PipeWriter writer)
        {
            Writer = writer;
        }

        public PipeWriter Writer { get; }
        public Stream Stream => throw new NotImplementedException();

        public Task CompleteAsync()
        {
            throw new NotImplementedException();
        }

        public void DisableBuffering()
        {
            throw new NotImplementedException();
        }

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
