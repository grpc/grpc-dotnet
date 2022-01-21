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

using BenchmarkDotNet.Attributes;
using Grpc.Core;

namespace Grpc.AspNetCore.Microbenchmarks.Server
{
    public class FailureStatusUnaryServerCallHandlerBenchmark : UnaryServerCallHandlerBenchmarkBase
    {
        public FailureStatusUnaryServerCallHandlerBenchmark()
        {
            Method = (service, request, context) => throw new RpcException(Status.DefaultCancelled);
            ExpectedStatus = "1";
        }

        [Benchmark]
        public Task HandleCallAsync()
        {
            return InvokeUnaryRequestAsync();
        }
    }
}
