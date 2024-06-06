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
using Chat;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Microbenchmarks.Server;

public class CompressedUnaryServerCallHandlerBenchmark : UnaryServerCallHandlerBenchmarkBase
{
    public CompressedUnaryServerCallHandlerBenchmark()
    {
        ResponseCompressionAlgorithm = TestCompressionProvider.Name;
        CompressionProviders = new List<ICompressionProvider>
        {
            new TestCompressionProvider()
        };
    }

    protected override void SetupHttpContext(HttpContext httpContext)
    {
        httpContext.Request.Headers[GrpcProtocolConstants.MessageEncodingHeader] = TestCompressionProvider.Name;
        httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "identity," + TestCompressionProvider.Name;
    }

    protected override byte[] GetMessageData(ChatMessage message)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append(GrpcProtocolConstants.MessageAcceptEncodingHeader, TestCompressionProvider.Name);

        var callContext = HttpContextServerCallContextHelper.CreateServerCallContext(
            httpContext,
            responseCompressionAlgorithm: ResponseCompressionAlgorithm,
            compressionProviders: CompressionProviders);

        var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, message, callContext);
        return ms.ToArray();
    }

    [Benchmark]
    public Task CompressedHandleCallAsync()
    {
        return InvokeUnaryRequestAsync();
    }
}
