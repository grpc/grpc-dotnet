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
using System.Buffers.Binary;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

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
            var payload = serializationContext.GetWrittenPayload();
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
            var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
            Assert.IsFalse(header.Compressed);
            Assert.AreEqual(1, header.Length);
            Assert.AreEqual(byte.MaxValue, serializationContext.GetWrittenPayload().Span[5]);

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
        var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
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
        var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
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
        var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
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

        Assert.AreEqual(1, ((ArrayBufferWriter<byte>)bufferWriter).Capacity);
        Assert.AreEqual(1, ((ArrayBufferWriter<byte>)bufferWriter).FreeCapacity);
        Assert.AreEqual(0, ((ArrayBufferWriter<byte>)bufferWriter).WrittenSpan.Length);

        var span = bufferWriter.GetSpan(3);
        span[0] = byte.MaxValue;
        bufferWriter.Advance(3);

        var ex = Assert.Throws<RpcException>(() => serializationContext.Complete())!;

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
            var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
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
            var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
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
        var header = DecodeHeader(serializationContext.GetWrittenPayload().Span);
        Assert.IsFalse(header.Compressed);
        Assert.AreEqual(1, header.Length);
    }

    [Test]
    public void Initialize_UnknownCompressionEncoding_ThrowError()
    {
        // Arrange
        var serializationContext = CreateSerializationContext(requestGrpcEncoding: "unknown");

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => serializationContext.Initialize())!;

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
        var ex = Assert.Throws<RpcException>(() => serializationContext.Complete(new byte[] { 1, 2, 3 }))!;

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
        var ex = Assert.Throws<InvalidOperationException>(() => serializationContext.GetWrittenPayload().ToArray())!;
        Assert.AreEqual("Serialization did not return a payload.", ex.Message);
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
        var ex = Assert.Throws<InvalidOperationException>(() => serializationContext.GetWrittenPayload().ToArray())!;
        Assert.AreEqual("Serialization did not return a payload.", ex.Message);
    }

    [Test]
    public void RentSerializationContext_SequentialUse_ReusesSameInstance()
    {
        // Arrange
        var call = CreateCall();

        // Act - first, fully completed lease.
        var lease1 = call.RentSerializationContext(new CallOptions());
        var context1 = lease1.Context;
        lease1.MarkReusable();
        lease1.Dispose();

        // A second, later (non-overlapping) lease.
        var lease2 = call.RentSerializationContext(new CallOptions());
        var context2 = lease2.Context;
        lease2.MarkReusable();
        lease2.Dispose();

        // Assert - context is cached and reused
        Assert.AreSame(context1, context2);
    }

    [Test]
    public void RentSerializationContext_OverlappingUse_DoesNotShareInstance()
    {
        // Arrange
        var call = CreateCall();

        // Act - first lease is rented but not yet returned, simulating a write still in flight 
        var lease1 = call.RentSerializationContext(new CallOptions());

        // A second lease overlaps with the first.
        var lease2 = call.RentSerializationContext(new CallOptions());

        try
        {
            // Assert - the two overlapping operations must never share the same mutable context/buffer
            Assert.AreNotSame(lease1.Context, lease2.Context);
        }
        finally
        {
            lease1.Dispose();
            lease2.Dispose();
        }
    }

    [Test]
    public void RentSerializationContext_NotMarkedReusable_IsNotCached()
    {
        // Arrange
        var call = CreateCall();

        // Act - a lease that is disposed without MarkReusable must not be handed back for reuse.
        var lease1 = call.RentSerializationContext(new CallOptions());
        var context1 = lease1.Context;
        lease1.Dispose();

        var lease2 = call.RentSerializationContext(new CallOptions());

        try
        {
            // Assert
            Assert.AreNotSame(context1, lease2.Context);
        }
        finally
        {
            lease2.MarkReusable();
            lease2.Dispose();
        }
    }

    [Test]
    public async Task WriteMessageAsync_ConcurrentWrites_PayloadsAreIndependent()
    {
        // Arrange
        var call = CreateCall();
        var stream1 = new BlockingWriteStream();
        var stream2 = new BlockingWriteStream();

        // Act
        var writeTask1 = stream1.WriteMessageAsync(call, (byte)1, SerializeByte, new CallOptions());
        var payload1 = await stream1.WriteStartedTask.DefaultTimeout();

        var writeTask2 = stream2.WriteMessageAsync(call, (byte)2, SerializeByte, new CallOptions());
        var payload2 = await stream2.WriteStartedTask.DefaultTimeout();

        try
        {
            // Assert
            Assert.AreEqual(1, payload1.Span[GrpcProtocolConstants.HeaderSize]);
            Assert.AreEqual(2, payload2.Span[GrpcProtocolConstants.HeaderSize]);
        }
        finally
        {
            stream1.Continue();
            stream2.Continue();
        }

        await Task.WhenAll(writeTask1, writeTask2).DefaultTimeout();

        static void SerializeByte(byte value, SerializationContext serializationContext)
        {
            serializationContext.SetPayloadLength(1);

            var bufferWriter = serializationContext.GetBufferWriter();
            bufferWriter.GetSpan(1)[0] = value;
            bufferWriter.Advance(1);

            serializationContext.Complete();
        }
    }

    private class TestGrpcCall : GrpcCall
    {
        public TestGrpcCall(CallOptions options, GrpcChannel channel) : base(options, channel)
        {
        }

        public override Type RequestType { get; } = typeof(int);
        public override Type ResponseType { get; } = typeof(string);
        public override CancellationToken CancellationToken { get; }
        public override Task<Status> CallTask => Task.FromResult(Status.DefaultCancelled);
    }

    private sealed class BlockingWriteStream : Stream
    {
        private readonly TaskCompletionSource<ReadOnlyMemory<byte>> _writeStartedTcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _continueTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ReadOnlyMemory<byte>> WriteStartedTask => _writeStartedTcs.Task;

        public void Continue() => _continueTcs.TrySetResult(null);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

#if NET462
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _writeStartedTcs.TrySetResult(buffer.AsMemory(offset, count));
            await _continueTcs.Task.ConfigureAwait(false);
        }
#else
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writeStartedTcs.TrySetResult(buffer);
            await _continueTcs.Task.ConfigureAwait(false);
        }
#endif
    }

    private GrpcCallSerializationContext CreateSerializationContext(string? requestGrpcEncoding = null, int? maxSendMessageSize = null)
    {
        var call = CreateCall(requestGrpcEncoding, maxSendMessageSize);
        return new GrpcCallSerializationContext(call);
    }

    private TestGrpcCall CreateCall(string? requestGrpcEncoding = null, int? maxSendMessageSize = null)
    {
        var channelOptions = new GrpcChannelOptions();
        channelOptions.MaxSendMessageSize = maxSendMessageSize;
        channelOptions.HttpHandler = new NullHttpHandler();

        var call = new TestGrpcCall(new CallOptions(), GrpcChannel.ForAddress("http://localhost", channelOptions));
        call.RequestGrpcEncoding = requestGrpcEncoding ?? "identity";

        return call;
    }

    private static (bool Compressed, int Length) DecodeHeader(ReadOnlySpan<byte> buffer)
    {
        return (buffer[0] == 1, (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(1)));
    }
}
