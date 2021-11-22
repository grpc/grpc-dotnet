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

using System.Buffers;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class PipeExtensionsPipelinesTests : PipeExtensionsTestsBase
    {
        private static readonly Marshaller<TestData> PipelineMarshaller = new Marshaller<TestData>(
            (TestData data, SerializationContext c) =>
            {
                c.SetPayloadLength(data.Span.Length);
                var bufferWriter = c.GetBufferWriter();
                bufferWriter.Write(data.Span);
                c.Complete();
            },
            (DeserializationContext c) =>
            {
                var sequence = c.PayloadAsReadOnlySequence();
                if (sequence.IsSingleSegment)
                {
                    return new TestData(sequence.First);
                }
                return new TestData(sequence.ToArray());
            });

        protected override Marshaller<TestData> TestDataMarshaller => PipelineMarshaller;
    }
}
