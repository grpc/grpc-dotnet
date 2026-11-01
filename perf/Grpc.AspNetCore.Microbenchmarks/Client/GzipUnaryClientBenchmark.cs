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
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.Core;

namespace Grpc.AspNetCore.Microbenchmarks.Client;

// The response isn't compressed so only the cost of compressing the request is measured.
public class GzipUnaryClientBenchmark : UnaryClientBenchmarkBase
{
    private readonly Metadata _compressionMetadata = new Metadata
    {
        { new Metadata.Entry("grpc-internal-encoding-request", "gzip") }
    };

    [Params(1024, 16 * 1024, 256 * 1024, 1024 * 1024)]
    public int MessageSize { get; set; }

    protected override string CreateContent()
    {
        return MessageHelpers.CreateCompressibleContent(MessageSize);
    }

    [Benchmark]
    public Task GzipSayHelloAsync()
    {
        return InvokeSayHelloAsync(new CallOptions(headers: _compressionMetadata));
    }
}
