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

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using Grpc.Net.Client.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests;

[TestFixture]
public class GrpcWebResponseStreamTests
{
    [Test]
    public async Task ReadAsync_EmptyMessage_ParseMessageAndTrailers()
    {
        // Arrange
        var data = Convert.FromBase64String("AAAAAACAAAAAEA0KZ3JwYy1zdGF0dXM6IDA=");
        var trailingHeaders = new TestHttpHeaders();
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

        // Act 1
        var contentHeaderData = new byte[5];
        var read1 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 1
        Assert.AreEqual(5, read1);
        Assert.AreEqual(0, contentHeaderData[0]);
        Assert.AreEqual(0, contentHeaderData[1]);
        Assert.AreEqual(0, contentHeaderData[2]);
        Assert.AreEqual(0, contentHeaderData[3]);
        Assert.AreEqual(0, contentHeaderData[4]);

        // Act 2
        var read2 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 2
        Assert.AreEqual(0, read2);
        Assert.AreEqual(1, trailingHeaders.Count());
        Assert.AreEqual("0", trailingHeaders.GetValues("grpc-status").Single());
    }

    [Test]
    public async Task ReadAsync_HasMessage_OneByteBuffer_ParseMessageAndTrailers()
    {
        // Arrange
        var header = new byte[] { 0, 0, 0, 1, 2 };
        var content = new byte[258];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % byte.MaxValue);
        }
        var trailer = new byte[] { 128, 0, 0, 0, 16, 13, 10, 103, 114, 112, 99, 45, 115, 116, 97, 116, 117, 115, 58, 32, 48 };
        var data = header.Concat(content).Concat(trailer).ToArray();

        var trailingHeaders = new TestHttpHeaders();
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

        // Act & Assert header
        var contentHeaderData = new byte[1];

        Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(258, responseStream._contentRemaining);
        Assert.AreEqual(GrpcWebResponseStream.ResponseState.Content, responseStream._state);

        // Act & Assert content
        var readContent = new List<byte>();
        while (responseStream._contentRemaining > 0)
        {
            Assert.AreEqual(1, await ReadAsync(responseStream, contentHeaderData));
            readContent.Add(contentHeaderData[0]);
        }
        
        CollectionAssert.AreEqual(content, readContent);

        // Act trailer
        var read2 = await ReadAsync(responseStream, contentHeaderData);

        // Assert trailer
        Assert.AreEqual(0, read2);
        Assert.AreEqual(1, trailingHeaders.Count());
        Assert.AreEqual("0", trailingHeaders.GetValues("grpc-status").Single());
    }

    [Test]
    public async Task ReadAsync_HasMessage_ZeroAndOneByteBuffer_ParseMessageAndTrailers()
    {
        // Arrange
        var header = new byte[] { 0, 0, 0, 1, 2 };
        var content = new byte[258];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % byte.MaxValue);
        }
        var trailer = new byte[] { 128, 0, 0, 0, 16, 13, 10, 103, 114, 112, 99, 45, 115, 116, 97, 116, 117, 115, 58, 32, 48 };
        var data = header.Concat(content).Concat(trailer).ToArray();

        var trailingHeaders = new TestHttpHeaders();
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

        // Act & Assert header
        var contentHeaderData = new byte[1];

        Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
        Assert.AreEqual(258, responseStream._contentRemaining);
        Assert.AreEqual(GrpcWebResponseStream.ResponseState.Content, responseStream._state);

        // Act & Assert content
        var readContent = new List<byte>();
        while (responseStream._contentRemaining > 0)
        {
            Assert.AreEqual(1, await ZeroAndContentReadAsync(responseStream, contentHeaderData));
            readContent.Add(contentHeaderData[0]);
        }

        CollectionAssert.AreEqual(content, readContent);

        // Act trailer
        var read2 = await ZeroAndContentReadAsync(responseStream, contentHeaderData);

        // Assert trailer
        Assert.AreEqual(0, read2);
        Assert.AreEqual(1, trailingHeaders.Count());
        Assert.AreEqual("0", trailingHeaders.GetValues("grpc-status").Single());

        static async Task<int> ZeroAndContentReadAsync(Stream stream, Memory<byte> data, CancellationToken cancellationToken = default)
        {
            // Zero byte read to ensure this works in the current stream state.
            var zeroRead = await ReadAsync(stream, Memory<byte>.Empty, cancellationToken);
            Assert.AreEqual(0, zeroRead);

            // Actual read.
            return await ReadAsync(stream, data, cancellationToken);
        }
    }

    [Test]
    public async Task ReadAsync_EmptyMessageAndTrailers_ParseMessageAndTrailers()
    {
        // Arrange
        var data = new byte[] { 0, 0, 0, 0, 0, 128, 0, 0, 0, 0 };
        var trailingHeaders = new TestHttpHeaders();
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

        // Act 1
        var contentHeaderData = new byte[5];
        var read1 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 1
        Assert.AreEqual(5, read1);
        Assert.AreEqual(0, contentHeaderData[0]);
        Assert.AreEqual(0, contentHeaderData[1]);
        Assert.AreEqual(0, contentHeaderData[2]);
        Assert.AreEqual(0, contentHeaderData[3]);
        Assert.AreEqual(0, contentHeaderData[4]);

        // Act 2
        var read2 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 2
        Assert.AreEqual(0, read2);
        Assert.AreEqual(0, trailingHeaders.Count());
    }

    [Test]
    public async Task ReadAsync_EmptyMessageAndTrailers_OneByteBuffer_ParseMessageAndTrailers()
    {
        // Arrange
        var data = new byte[] { 0, 0, 0, 0, 0, 128, 0, 0, 0, 0 };
        var trailingHeaders = new TestHttpHeaders();
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);
        var contentHeaderData = new byte[1];

        await ReadByteAsync(responseStream, contentHeaderData);
        await ReadByteAsync(responseStream, contentHeaderData);
        await ReadByteAsync(responseStream, contentHeaderData);
        await ReadByteAsync(responseStream, contentHeaderData);
        await ReadByteAsync(responseStream, contentHeaderData);

        // Act 2
        var read2 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 2
        Assert.AreEqual(0, read2);
        Assert.AreEqual(0, trailingHeaders.Count());

        async Task ReadByteAsync(GrpcWebResponseStream responseStream, byte[] buffer)
        {
            // Act
            var read = await ReadAsync(responseStream, buffer);

            // Assert
            Assert.AreEqual(1, read);
            Assert.AreEqual(0, buffer[0]);
        }
    }

    [Test]
    public async Task ReadAsync_ReadContentWithLargeBuffer_ParseMessageAndTrailers()
    {
        // Arrange
        var data = new byte[] { 0, 0, 0, 0, 1, 99, 128, 0, 0, 0, 0 };
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, new TestHttpHeaders());

        // Act 1
        var contentHeaderData = new byte[1024];
        var read1 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 1
        Assert.AreEqual(5, read1);
        Assert.AreEqual(0, contentHeaderData[0]);
        Assert.AreEqual(0, contentHeaderData[1]);
        Assert.AreEqual(0, contentHeaderData[2]);
        Assert.AreEqual(0, contentHeaderData[3]);
        Assert.AreEqual(1, contentHeaderData[4]);
        Assert.AreEqual(1, responseStream._contentRemaining);
        Assert.AreEqual(GrpcWebResponseStream.ResponseState.Content, responseStream._state);

        // Act 2
        var read2 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 2
        Assert.AreEqual(1, read2);
        Assert.AreEqual(99, contentHeaderData[0]);
        Assert.AreEqual(GrpcWebResponseStream.ResponseState.Ready, responseStream._state);

        // Act 2
        var read3 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 2
        Assert.AreEqual(0, read3);
    }

    [Test]
    public async Task ReadAsync_HasContentAfterTrailers_Errors()
    {
        // Arrange
        var data = new byte[] { 0, 0, 0, 0, 0, 128, 0, 0, 0, 0, 1 };
        var ms = new MemoryStream(data);
        var responseStream = new GrpcWebResponseStream(ms, new TestHttpHeaders());

        // Act 1
        var contentHeaderData = new byte[5];
        var read1 = await ReadAsync(responseStream, contentHeaderData);

        // Assert 1
        Assert.AreEqual(5, read1);
        Assert.AreEqual(0, contentHeaderData[0]);
        Assert.AreEqual(0, contentHeaderData[1]);
        Assert.AreEqual(0, contentHeaderData[2]);
        Assert.AreEqual(0, contentHeaderData[3]);
        Assert.AreEqual(0, contentHeaderData[4]);

        // Act 2
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(responseStream, contentHeaderData));

        // Assert 2
        Assert.AreEqual("Unexpected data after trailers.", ex.Message);
    }

    private class TestHttpHeaders : HttpHeaders
    {
    }

    [TestCase("", "")]
    [TestCase(" ", "")]
    [TestCase(" a ", "a")]
    [TestCase("      ", "")]
    [TestCase("a      ", "a")]
    [TestCase("      a", "a")]
    [TestCase("a     a", "a     a")]
    [TestCase("  a     a  ", "a     a")]
    public void Trim(string initial, string expected)
    {
        var result = GrpcWebResponseStream.Trim(Encoding.UTF8.GetBytes(initial));
        var s = Encoding.UTF8.GetString(result.ToArray());
        Assert.AreEqual(expected, s);
    }

    private static Task<int> ReadAsync(Stream stream, Memory<byte> data, CancellationToken cancellationToken = default)
    {
#if NET462
        var success = MemoryMarshal.TryGetArray<byte>(data, out var segment);
        Debug.Assert(success);
        return stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
#else
        return stream.ReadAsync(data, cancellationToken).AsTask();
#endif
    }
}
