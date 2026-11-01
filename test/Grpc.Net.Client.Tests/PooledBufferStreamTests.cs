#region Copyright notice and license

// Copyright 2026 The gRPC Authors
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
using System.IO.Compression;
using Grpc.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class PooledBufferStreamTests
{
    [Test]
    public void WrittenSpan_NoWrites_Empty()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.AreEqual(0, stream.WrittenSpan.Length);
        Assert.AreEqual(0, stream.Length);
    }

    [Test]
    public void Write_LargerThanCapacity_GrowsAndPreservesData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var expected = CreateData(100_000);

        // Act
        for (var i = 0; i < expected.Length; i += 1000)
        {
            stream.Write(expected, i, 1000);
        }

        // Assert
        Assert.AreEqual(expected.Length, stream.Length);
        CollectionAssert.AreEqual(expected, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void WriteByte_MultipleBytes_WritesInOrder()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act
        stream.WriteByte(1);
        stream.WriteByte(2);

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public async Task WriteAsync_Data_WritesData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var expected = CreateData(1000);

        // Act
        await stream.WriteAsync(expected, 0, expected.Length);

        // Assert
        CollectionAssert.AreEqual(expected, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Release_AfterWrite_DiscardsDataAndAllowsReuse()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.WriteByte(1);

        // Act
        stream.Release();

        // Assert
        Assert.AreEqual(0, stream.Length);
        Assert.AreEqual(0, stream.WrittenSpan.Length);

        stream.WriteByte(2);
        CollectionAssert.AreEqual(new byte[] { 2 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Release_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.WriteByte(1);

        // Act & Assert
        stream.Release();
        stream.Release();
    }

    [Test]
    public void Dispose_AfterWrite_DataStillAvailable()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.WriteByte(1);

        // Act
        stream.Dispose();

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Write_NegativeCount_Throws()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(new byte[1], 0, -1));
    }

    [Test]
    public void Read_NegativeCount_Throws()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Assert.AreEqual(0, stream.Read(new byte[1], 0, -1)));
    }

    [Test]
    public void ReadByte_AfterWrite_ReturnsByteAndAdvances()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.WriteByte(1);
        stream.Position = 0;

        // Act
        var value = stream.ReadByte();

        // Assert
        Assert.AreEqual(1, value);
        Assert.AreEqual(1, stream.Position);

        stream.Release();
    }

    [Test]
    public async Task ReadAsync_Data_ReadsData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2 }, 0, 2);
        stream.Position = 0;
        var buffer = new byte[2];

        // Act
        var read = await stream.ReadAsync(buffer, 0, buffer.Length);

        // Assert
        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, buffer);

        stream.Release();
    }

    [Test]
    public void AsyncMethods_CancelledToken_ReturnCancelledTask()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var cancellationToken = new CancellationToken(canceled: true);

        // Act & Assert
        Assert.IsTrue(stream.WriteAsync(new byte[1], 0, 1, cancellationToken).IsCanceled);
        Assert.IsTrue(stream.ReadAsync(new byte[1], 0, 1, cancellationToken).IsCanceled);
        Assert.IsTrue(stream.FlushAsync(cancellationToken).IsCanceled);
        Assert.AreEqual(0, stream.Length);
    }

    [Test]
    public void AsyncMethods_InvalidArguments_ReturnFaultedTask()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.IsInstanceOf<ArgumentOutOfRangeException>(stream.WriteAsync(new byte[1], 0, -1).Exception!.InnerException);
        Assert.IsInstanceOf<ArgumentOutOfRangeException>(stream.ReadAsync(new byte[1], 0, -1).Exception!.InnerException);
    }

#if !NET462
    [Test]
    public void Read_Span_ReadsDataAndAdvances()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
        stream.Position = 1;
        var buffer = new byte[2];

        // Act
        var read = stream.Read(buffer.AsSpan());

        // Assert
        Assert.AreEqual(2, read);
        Assert.AreEqual(3, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 2, 3 }, buffer);

        stream.Release();
    }

    [Test]
    public async Task ReadAsync_Memory_ReadsData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2 }, 0, 2);
        stream.Position = 0;
        var buffer = new byte[2];

        // Act
        var read = await stream.ReadAsync(buffer.AsMemory());

        // Assert
        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, buffer);

        stream.Release();
    }

    [Test]
    public async Task WriteAsync_Memory_WritesData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var expected = CreateData(1000);

        // Act
        await stream.WriteAsync(expected.AsMemory());

        // Assert
        CollectionAssert.AreEqual(expected, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void AsyncMemoryMethods_CancelledToken_ReturnCancelledTask()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var cancellationToken = new CancellationToken(canceled: true);

        // Act & Assert
        Assert.IsTrue(stream.WriteAsync(new byte[1].AsMemory(), cancellationToken).IsCanceled);
        Assert.IsTrue(stream.ReadAsync(new byte[1].AsMemory(), cancellationToken).IsCanceled);
        Assert.AreEqual(0, stream.Length);
    }
#endif

    [Test]
    public void Write_ZeroBytes_NoData()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act
        stream.Write(Array.Empty<byte>(), 0, 0);

        // Assert
        Assert.AreEqual(0, stream.Length);
    }

    [Test]
    public void Write_AfterSeekToStart_OverwritesAndKeepsLength()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);

        // Act
        stream.Seek(0, SeekOrigin.Begin);
        stream.WriteByte(9);

        // Assert
        Assert.AreEqual(1, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 9, 2, 3 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Write_AfterSeekPastEnd_ZeroesGap()
    {
        // Arrange
        DirtyPooledBuffers();
        var stream = new PooledBufferStream();
        stream.WriteByte(1);

        // Act
        stream.Position = 4;
        stream.WriteByte(2);

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 2 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Read_AfterWrite_ReturnsDataUntilEnd()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
        stream.Position = 1;
        var buffer = new byte[10];

        // Act
        var read = stream.Read(buffer, 0, buffer.Length);

        // Assert
        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new byte[] { 2, 3 }, buffer.Take(read).ToArray());
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
        Assert.AreEqual(-1, stream.ReadByte());

        stream.Release();
    }

    [Test]
    public void Read_PositionPastEnd_ReturnsZero()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Position = 10;

        // Act & Assert
        Assert.AreEqual(0, stream.Read(new byte[1], 0, 1));
    }

    [Test]
    public void Seek_FromCurrentAndEnd_ReturnsPosition()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);

        // Act & Assert
        Assert.AreEqual(1, stream.Seek(-2, SeekOrigin.End));
        Assert.AreEqual(2, stream.Seek(1, SeekOrigin.Current));

        stream.Release();
    }

    [Test]
    public void Seek_BeforeStart_Throws()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
    }

    [Test]
    public void Position_Negative_Throws()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    }

    [Test]
    public void SetLength_Shorter_TruncatesAndMovesPosition()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1, 2, 3 }, 0, 3);

        // Act
        stream.SetLength(1);

        // Assert
        Assert.AreEqual(1, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 1 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void SetLength_Longer_ZeroesNewBytes()
    {
        // Arrange
        DirtyPooledBuffers();
        var stream = new PooledBufferStream();
        stream.WriteByte(1);

        // Act
        stream.SetLength(4);

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void GZipStream_DisposesOutput_CompressedDataCanBeRead()
    {
        // Arrange
        var stream = new PooledBufferStream();
        var expected = CreateData(100_000);

        // Act
        using (var compressionStream = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: false))
        {
            compressionStream.Write(expected, 0, expected.Length);
        }

        // Assert
        var decompressed = new MemoryStream();
        using (var decompressionStream = new GZipStream(new MemoryStream(stream.WrittenSpan.ToArray()), CompressionMode.Decompress))
        {
            decompressionStream.CopyTo(decompressed);
        }
        CollectionAssert.AreEqual(expected, decompressed.ToArray());

        stream.Release();
    }

    private static void DirtyPooledBuffers()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        buffer.AsSpan().Fill(byte.MaxValue);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private static byte[] CreateData(int length)
    {
        var data = new byte[length];
        new Random(1).NextBytes(data);
        return data;
    }
}
