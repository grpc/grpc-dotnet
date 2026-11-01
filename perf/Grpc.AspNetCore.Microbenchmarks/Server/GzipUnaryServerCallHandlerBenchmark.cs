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

using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Chat;
using Google.Protobuf;
using Grpc.AspNetCore.Microbenchmarks.Internal;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Net.Compression;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Microbenchmarks.Server;

// The request isn't compressed so only the cost of compressing the response is measured.
public class GzipUnaryServerCallHandlerBenchmark : UnaryServerCallHandlerBenchmarkBase
{
    private const string GzipEncoding = "gzip";

    [Params(1024, 16 * 1024, 256 * 1024, 1024 * 1024)]
    public int MessageSize { get; set; }

    public GzipUnaryServerCallHandlerBenchmark()
    {
        ResponseCompressionAlgorithm = GzipEncoding;
        CompressionProviders = new List<ICompressionProvider>
        {
            new GzipCompressionProvider(CompressionLevel.Fastest)
        };
    }

    protected override string CreateContent()
    {
        return MessageHelpers.CreateCompressibleContent(MessageSize);
    }

    // Serialize the same way as generated code.
    protected override Marshaller<ChatMessage> CreateMarshaller()
    {
        return Marshallers.Create<ChatMessage>(
            (message, context) =>
            {
                context.SetPayloadLength(message.CalculateSize());
                message.WriteTo(context.GetBufferWriter());
                context.Complete();
            },
            context => new ChatMessage());
    }

    protected override void SetupHttpContext(HttpContext httpContext)
    {
        httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "identity," + GzipEncoding;
    }

    [Benchmark]
    public Task GzipHandleCallAsync()
    {
        return InvokeUnaryRequestAsync();
    }
}
