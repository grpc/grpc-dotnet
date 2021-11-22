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
using Grpc.Core;

namespace Grpc.AspNetCore.Microbenchmarks.Server
{
    public class PipelinesUnaryServerCallHandlerBenchmark : UnaryServerCallHandlerBenchmarkBase
    {
        protected override Marshaller<ChatMessage> CreateMarshaller()
        {
            var marshaller = new Marshaller<ChatMessage>(
                (ChatMessage data, SerializationContext c) =>
                {
                    var size = data.CalculateSize();
                    c.SetPayloadLength(size);
                    var writer = c.GetBufferWriter();
                    writer.GetSpan(size);
                    writer.Advance(size);
                    c.Complete();
                },
                (DeserializationContext c) =>
                {
                    c.PayloadAsReadOnlySequence();
                    return new ChatMessage();
                });

            return marshaller;
        }

        [Benchmark]
        public Task PipelinesHandleCallAsync()
        {
            return InvokeUnaryRequestAsync();
        }
    }
}
