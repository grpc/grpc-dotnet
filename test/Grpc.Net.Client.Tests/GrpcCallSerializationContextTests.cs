﻿#region Copyright notice and license

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
using System.Buffers.Binary;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcCallSerializationContextTests
    {
        [TestCase(1)]
        [TestCase(5)]
        public void GetBufferWriter_NoLength_NoCompressionEncoding_WritesPayload(int iterations)
        {
            // Arrange
            var serializationContext = CreateSerializationContext();

            for (var i = 0; i < iterations; i++)
            {
                serializationContext.Initialize();

                // Act
                var bufferWriter = serializationContext.GetBufferWriter();
                var span = bufferWriter.GetSpan(1);
                span[0] = byte.MaxValue;
                bufferWriter.Advance(1);

                serializationContext.Complete();

                // Assert
                Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
                var header = DecodeHeader(payload.Span);
                Assert.IsFalse(header.Compressed);
                Assert.AreEqual(1, header.Length);
                Assert.AreEqual(byte.MaxValue, payload.Span[5]);

                serializationContext.Reset();
            }
        }

        [TestCase(1)]
        [TestCase(5)]
        public void GetBufferWriter_HasLength_NoCompressionEncoding_WritesPayload(int iterations)
        {
            // Arrange
            var serializationContext = CreateSerializationContext();

            for (var i = 0; i < iterations; i++)
            {
                serializationContext.Initialize();

                // Act
                serializationContext.SetPayloadLength(1);
                var bufferWriter = serializationContext.GetBufferWriter();
                var span = bufferWriter.GetSpan(1);
                span[0] = byte.MaxValue;
                bufferWriter.Advance(1);

                serializationContext.Complete();

                // Assert
                Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
                var header = DecodeHeader(payload.Span);
                Assert.IsFalse(header.Compressed);
                Assert.AreEqual(1, header.Length);
                Assert.AreEqual(byte.MaxValue, payload.Span[5]);

                serializationContext.Reset();
            }
        }

        [Test]
        public void GetBufferWriter_NoLength_HasCompressionEncoding_WritesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip");
            serializationContext.Initialize();

            // Act
            var bufferWriter = serializationContext.GetBufferWriter();
            var span = bufferWriter.GetSpan(1);
            span[0] = byte.MaxValue;
            bufferWriter.Advance(1);

            serializationContext.Complete();

            // Assert
            Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
            var header = DecodeHeader(payload.Span);
            Assert.IsTrue(header.Compressed);
            Assert.AreEqual(21, header.Length);
        }

        [Test]
        public void GetBufferWriter_HasLength_HasCompressionEncoding_WritesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip");
            serializationContext.Initialize();

            // Act
            serializationContext.SetPayloadLength(1);
            var bufferWriter = serializationContext.GetBufferWriter();
            var span = bufferWriter.GetSpan(1);
            span[0] = byte.MaxValue;
            bufferWriter.Advance(1);

            serializationContext.Complete();

            // Assert
            Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
            var header = DecodeHeader(payload.Span);
            Assert.IsTrue(header.Compressed);
            Assert.AreEqual(21, header.Length);
        }

        [Test]
        public void GetBufferWriter_HasLength_HasCompressionEncoding_NoCompressionOptions_WritesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip");
            serializationContext.CallOptions = new CallOptions(writeOptions: new WriteOptions(WriteFlags.NoCompress));
            serializationContext.Initialize();

            // Act
            serializationContext.SetPayloadLength(1);
            var bufferWriter = serializationContext.GetBufferWriter();
            var span = bufferWriter.GetSpan(1);
            span[0] = byte.MaxValue;
            bufferWriter.Advance(1);

            serializationContext.Complete();

            // Assert
            Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
            var header = DecodeHeader(payload.Span);
            Assert.IsFalse(header.Compressed);
            Assert.AreEqual(1, header.Length);
        }

        [Test]
        public void GetBufferWriter_ExceedSendMessageSize_ThrowError()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip", maxSendMessageSize: 2);
            serializationContext.Initialize();

            // Act
            serializationContext.SetPayloadLength(1);
            var bufferWriter = serializationContext.GetBufferWriter();
            var span = bufferWriter.GetSpan(3);
            span[0] = byte.MaxValue;
            bufferWriter.Advance(3);

            var ex = Assert.Throws<RpcException>(() => serializationContext.Complete());

            // Assert
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void Complete_NoCompressionEncoding_WritesPayload(int iterations)
        {
            // Arrange
            var serializationContext = CreateSerializationContext();

            for (var i = 0; i < iterations; i++)
            {
                serializationContext.Initialize();

                // Act
                serializationContext.Complete(new byte[] { 1 });

                // Assert
                Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
                var header = DecodeHeader(payload.Span);
                Assert.IsFalse(header.Compressed);
                Assert.AreEqual(1, header.Length);

                serializationContext.Reset();
            }
        }

        [TestCase(1)]
        [TestCase(5)]
        public void Complete_HasCompressionEncoding_WritesPayload(int iterations)
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip");

            for (var i = 0; i < iterations; i++)
            {
                serializationContext.Initialize();

                // Act
                serializationContext.Complete(new byte[] { 1 });

                // Assert
                Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
                var header = DecodeHeader(payload.Span);
                Assert.IsTrue(header.Compressed);
                Assert.AreEqual(21, header.Length);

                serializationContext.Reset();
            }
        }

        [Test]
        public void Complete_HasCompressionEncoding_NoCompressionOptions_WritesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip");
            serializationContext.CallOptions = new CallOptions(writeOptions: new WriteOptions(WriteFlags.NoCompress));
            serializationContext.Initialize();

            // Act
            serializationContext.Complete(new byte[] { 1 });

            // Assert
            Assert.IsTrue(serializationContext.TryGetPayload(out var payload));
            var header = DecodeHeader(payload.Span);
            Assert.IsFalse(header.Compressed);
            Assert.AreEqual(1, header.Length);
        }

        [Test]
        public void Initialize_UnknownCompressionEncoding_ThrowError()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "unknown");

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => serializationContext.Initialize());

            // Assert
            Assert.AreEqual("Could not find compression provider for 'unknown'.", ex.Message);
        }

        [Test]
        public void Complete_ExceedSendMessageSize_ThrowError()
        {
            // Arrange
            var serializationContext = CreateSerializationContext(requestGrpcEncoding: "gzip", maxSendMessageSize: 2);
            serializationContext.Initialize();

            // Act
            var ex = Assert.Throws<RpcException>(() => serializationContext.Complete(new byte[] { 1, 2, 3 }));

            // Assert
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [Test]
        public void Reset_AfterComplete_RemovesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext();
            serializationContext.Initialize();
            serializationContext.Complete(new byte[] { 1 });

            // Act
            serializationContext.Reset();

            // Assert
            Assert.IsFalse(serializationContext.TryGetPayload(out _));
        }

        [Test]
        public void Reset_AfterGetBufferWriter_RemovesPayload()
        {
            // Arrange
            var serializationContext = CreateSerializationContext();
            serializationContext.Initialize();

            var bufferWriter = serializationContext.GetBufferWriter();
            var span = bufferWriter.GetSpan(1);
            span[0] = byte.MaxValue;
            bufferWriter.Advance(1);

            serializationContext.Complete();

            // Act
            serializationContext.Reset();

            // Assert
            Assert.IsFalse(serializationContext.TryGetPayload(out _));
        }

        private class TestGrpcCall : GrpcCall
        {
            public TestGrpcCall(CallOptions options, GrpcChannel channel) : base(options, channel)
            {
            }

            public override Type RequestType { get; } = typeof(int);
            public override Type ResponseType { get; } = typeof(string);
        }

        private GrpcCallSerializationContext CreateSerializationContext(string? requestGrpcEncoding = null, int? maxSendMessageSize = null)
        {
            var channelOptions = new GrpcChannelOptions();
            channelOptions.MaxSendMessageSize = maxSendMessageSize;

            var call = new TestGrpcCall(new CallOptions(), GrpcChannel.ForAddress("http://localhost", channelOptions));
            call.RequestGrpcEncoding = requestGrpcEncoding ?? "identity";

            return new GrpcCallSerializationContext(call);
        }

        private static (bool Compressed, int Length) DecodeHeader(ReadOnlySpan<byte> buffer)
        {
            return (buffer[0] == 1, (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(1)));
        }
    }
}
