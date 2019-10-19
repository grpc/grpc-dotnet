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

using Grpc.Shared;
using NUnit.Framework;
using System;
using System.Runtime.InteropServices;

namespace Grpc.AspNetCore.FunctionalTests.Shared
{
    [TestFixture]
    public class DefaultSerializationContextTests
    {
        private readonly DefaultSerializationContext _serializationContext
            = new DefaultSerializationContext();

        private DefaultSerializationContext GetContext()
        {
            _serializationContext.Reset();
            return _serializationContext;
        }

        [TestCase]
        public void IncompleteShouldNotReturnPayload()
        {
            var ctx = GetContext();
            Assert.False(ctx.TryGetPayload(out _));
        }

        [TestCase]
        public void ParameterlessCompleteWithoutGetBufferShouldThrow()
        {
            var ctx = GetContext();
            Assert.Throws<InvalidOperationException>(() => ctx.Complete());
        }

        [TestCase]
        public void ParameterlessCompleteWithGetBufferShouldReturnEmpty()
        {
            var ctx = GetContext();
            _ = ctx.GetBufferWriter();
            ctx.Complete();
            Assert.True(ctx.TryGetPayload(out var payload));
            Assert.True(payload.IsEmpty);
        }

        [TestCase]
        public void CanCallGetBufferWriterMultipleTImes()
        {
            var ctx = GetContext();
            _ = ctx.GetBufferWriter();
            _ = ctx.GetBufferWriter();
            _ = ctx.GetBufferWriter();
            ctx.Complete();
            Assert.True(ctx.TryGetPayload(out var payload));
            Assert.True(payload.IsEmpty);
        }

        [TestCase]
        public void CompleteBufferWriterMultipleTimesShouldThrow()
        {
            var ctx = GetContext();
            _ = ctx.GetBufferWriter();
            ctx.Complete();
            Assert.Throws<InvalidOperationException>(() =>
            {
                ctx.Complete();
            });
        }

        [TestCase]
        public void ArrayCompleteWithoutGetBufferShouldThrow()
        {
            var ctx = GetContext();
            _ = ctx.GetBufferWriter();
            Assert.Throws<InvalidOperationException>(() =>
            {
                ctx.Complete(Array.Empty<byte>());
            });
        }

        [TestCase]
        public void BufferWriterShouldWork()
        {
            var ctx = GetContext();
            var writer = ctx.GetBufferWriter();
            var span = writer.GetMemory(5).Span;
            span[0] = 0x00;
            span[1] = 0x01;
            span[2] = 0x02;
            writer.Advance(3);
            span = writer.GetSpan(10);
            span[0] = 0x03;
            span[1] = 0x04;
            writer.Advance(2);
            ctx.Complete();
            Assert.True(ctx.TryGetPayload(out var mem));
            Assert.AreEqual(5, mem.Length);
            var rospan = mem.Span;
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(i, rospan[i]);
        }

        [TestCase]
        public void CompleteArrayShouldWork()
        {
            var ctx = GetContext();
            var arr = new byte[42];
            ctx.Complete(arr);
            Assert.True(ctx.TryGetPayload(out var payload));
            Assert.AreEqual(42, payload.Length);
            Assert.True(MemoryMarshal.TryGetArray(payload, out var segment));
            Assert.AreSame(arr, segment.Array);
            Assert.AreEqual(0, segment.Offset);
            Assert.AreEqual(42, segment.Count);
        }

        [TestCase]
        public void CompleteArrayTwiceShouldThrow()
        {
            var ctx = GetContext();
            ctx.Complete(Array.Empty<byte>());
            Assert.Throws<InvalidOperationException>(() =>
            {
                ctx.Complete(Array.Empty<byte>());
            });
        }
    }
}
