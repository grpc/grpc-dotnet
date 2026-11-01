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
    public void Release_AfterWrite_DiscardsData()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1 }, 0, 1);

        // Act
        stream.Release();

        // Assert
        Assert.AreEqual(0, stream.Length);
        Assert.AreEqual(0, stream.WrittenSpan.Length);
    }

    [Test]
    public void Release_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1 }, 0, 1);

        // Act & Assert
        stream.Release();
        stream.Release();
    }

    [Test]
    public void Dispose_AfterWrite_DataStillAvailable()
    {
        // Arrange
        var stream = new PooledBufferStream();
        stream.Write(new byte[] { 1 }, 0, 1);

        // Act
        stream.Dispose();

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1 }, stream.WrittenSpan.ToArray());

        stream.Release();
    }

    [Test]
    public void Stream_IsWriteOnly()
    {
        // Arrange
        var stream = new PooledBufferStream();

        // Act & Assert
        Assert.IsTrue(stream.CanWrite);
        Assert.IsFalse(stream.CanRead);
        Assert.IsFalse(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => Assert.AreEqual(0, stream.Read(new byte[1], 0, 1)));
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
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

    private static byte[] CreateData(int length)
    {
        var data = new byte[length];
        new Random(1).NextBytes(data);
        return data;
    }
}
