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
using System.Runtime.InteropServices;
using System.Text;
using Grpc.Net.Client.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests;

[TestFixture]
public class Base64ResponseStreamTests
{
    [Test]
    public async Task ReadAsync_ReadLargeData_Success()
    {
        // Arrange
        var headerData = new byte[] { 0, 0, 1, 0, 4 };
        var length = 65540;
        var content = CreateTestData(length);

        var messageContent = Encoding.UTF8.GetBytes(Convert.ToBase64String(headerData.Concat(content).ToArray()));
        var messageCount = 3;

        var streamContent = new List<byte>();
        for (var i = 0; i < messageCount; i++)
        {
            streamContent.AddRange(messageContent);
        }

        var ms = new LimitedReadMemoryStream(streamContent.ToArray(), 3);
        var base64Stream = new Base64ResponseStream(ms);

        for (var i = 0; i < messageCount; i++)
        {
            // Assert 1
            var resolvedHeaderData = await ReadContent(base64Stream, 5, CancellationToken.None);
            // Act 1
            CollectionAssert.AreEqual(headerData, resolvedHeaderData);

            // Assert 2
            var resolvedContentData = await ReadContent(base64Stream, (uint)length, CancellationToken.None);
            // Act 2
            CollectionAssert.AreEqual(content, resolvedContentData);
        }
    }

    private class LimitedReadMemoryStream : MemoryStream
    {
        private readonly int _maxReadLength;

        public LimitedReadMemoryStream(byte[] buffer, int maxReadLength) : base(buffer)
        {
            _maxReadLength = maxReadLength;
        }

#if NET462
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return base.ReadAsync(buffer, offset, Math.Min(count, _maxReadLength), cancellationToken);
        }
#else
        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            var resolvedDestination = destination.Slice(0, Math.Min(_maxReadLength, destination.Length));
            return base.ReadAsync(resolvedDestination, cancellationToken);
        }
#endif
    }

    private static byte[] CreateTestData(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i; // Will loop around back to zero
        }
        return data;
    }

    [Test]
    public void DecodeBase64DataFragments_MultipleFragments_Success()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=gAAAABANCmdycGMtc3RhdHVzOiAw");

        // Act
        var bytesWritten = Base64ResponseStream.DecodeBase64DataFragments(data);

        // Assert
        Assert.AreEqual(32, bytesWritten);

        var expected = Convert.FromBase64String("AAAAAAYKBHRlc3Q=")
            .Concat(Convert.FromBase64String("gAAAABANCmdycGMtc3RhdHVzOiAw"))
            .ToArray();
        var resolvedData = data.AsSpan(0, bytesWritten).ToArray();

        CollectionAssert.AreEqual(expected, resolvedData);
    }

    [Test]
    public async Task ReadAsync_MultipleReads_SmallDataSingleRead_Success()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=gAAAABANCmdycGMtc3RhdHVzOiAw");

        var ms = new LimitedReadMemoryStream(data, 3);
        var base64Stream = new Base64ResponseStream(ms);

        // Act 1
        var messageHeadData = await ReadContent(base64Stream, 5);

        // Assert 1
        Assert.AreEqual(0, messageHeadData[0]);
        Assert.AreEqual(0, messageHeadData[1]);
        Assert.AreEqual(0, messageHeadData[2]);
        Assert.AreEqual(0, messageHeadData[3]);
        Assert.AreEqual(6, messageHeadData[4]);

        // Act 2
        var messageData = await ReadContent(base64Stream, 6);

        // Assert 2
        var s = Encoding.UTF8.GetString(messageData.AsSpan(2).ToArray());
        Assert.AreEqual("test", s);

        // Act 3
        var footerHeadData = await ReadContent(base64Stream, 5);

        // Assert 3
        Assert.AreEqual(128, footerHeadData[0]);
        Assert.AreEqual(0, footerHeadData[1]);
        Assert.AreEqual(0, footerHeadData[2]);
        Assert.AreEqual(0, footerHeadData[3]);
        Assert.AreEqual(16, footerHeadData[4]);

        // Act 3
        StreamReader r = new StreamReader(base64Stream, Encoding.UTF8);
        var footerText = await r.ReadToEndAsync().DefaultTimeout();

        Assert.AreEqual("\r\ngrpc-status: 0", footerText);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    public async Task ReadAsync_MultipleReadsWithLimitedData_Success(int readSize)
    {
        // Arrange
        var base64Data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=gAAAABANCmdycGMtc3RhdHVzOiAw");

        var ms = new LimitedReadMemoryStream(base64Data, readSize);
        var base64Stream = new Base64ResponseStream(ms);

        // Act 1
        var messageHeadData = await ReadContent(base64Stream, 5);

        // Assert 1
        Assert.AreEqual(0, messageHeadData[0]);
        Assert.AreEqual(0, messageHeadData[1]);
        Assert.AreEqual(0, messageHeadData[2]);
        Assert.AreEqual(0, messageHeadData[3]);
        Assert.AreEqual(6, messageHeadData[4]);

        // Act 2
        var messageData = await ReadContent(base64Stream, 6);

        // Assert 2
        var s = Encoding.UTF8.GetString(messageData.AsSpan(2).ToArray());
        Assert.AreEqual("test", s);

        // Act 3
        var footerHeadData = await ReadContent(base64Stream, 5);

        // Assert 3
        Assert.AreEqual(128, footerHeadData[0]);
        Assert.AreEqual(0, footerHeadData[1]);
        Assert.AreEqual(0, footerHeadData[2]);
        Assert.AreEqual(0, footerHeadData[3]);
        Assert.AreEqual(16, footerHeadData[4]);

        // Act 3
        var footerContentData = await ReadContent(base64Stream, 16);

        var expected = Convert.FromBase64String("AAAAAAYKBHRlc3Q=")
           .Concat(Convert.FromBase64String("gAAAABANCmdycGMtc3RhdHVzOiAw"))
           .ToArray();
        var actual = messageHeadData
            .Concat(messageData)
            .Concat(footerHeadData)
            .Concat(footerContentData)
            .ToArray();

        Assert.AreEqual(expected, actual);
    }

    private static async Task<byte[]> ReadContent(Stream responseStream, uint length, CancellationToken cancellationToken = default)
    {
        // Read message content until content length is reached
        byte[] messageData;
        if (length > 0)
        {
            var received = 0;
            int read;
            messageData = new byte[length];
            while ((read = await ReadAsync(responseStream, messageData.AsMemory(received, messageData.Length - received), cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;

                if (received == messageData.Length)
                {
                    break;
                }
            }
        }
        else
        {
            messageData = Array.Empty<byte>();
        }

        return messageData;
    }

    [Test]
    public async Task ReadAsync_SmallDataSingleRead_Success()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello world");

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(Convert.ToBase64String(data)));
        var base64Stream = new Base64ResponseStream(ms);

        // Act
        var buffer = new byte[1024];
        var read = await ReadAsync(base64Stream, buffer);

        // Assert
        Assert.AreEqual(read, data.Length);
        CollectionAssert.AreEqual(data, data.AsSpan(0, read).ToArray());
    }

    [Test]
    public async Task ReadAsync_SingleByteReads_Success()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello world");

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(Convert.ToBase64String(data)));
        var base64Stream = new Base64ResponseStream(ms);

        // Act
        var allData = new List<byte>();
        var buffer = new byte[1];

        int read;
        while ((read = await ReadAsync(base64Stream, buffer)) > 0)
        {
            allData.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        var readData = allData.ToArray();

        // Assert
        CollectionAssert.AreEqual(data, readData);
    }

    [Test]
    public async Task ReadAsync_TwoByteReads_Success()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello world");

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(Convert.ToBase64String(data)));
        var base64Stream = new Base64ResponseStream(ms);

        // Act
        var allData = new List<byte>();
        var buffer = new byte[2];

        int read;
        while ((read = await ReadAsync(base64Stream, buffer)) > 0)
        {
            allData.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        var readData = allData.ToArray();

        // Assert
        CollectionAssert.AreEqual(data, readData);
    }

    [TestCase("Hello world", 1)]
    [TestCase("Hello world", 2)]
    [TestCase("Hello world", 3)]
    [TestCase("Hello world", 4)]
    [TestCase("Hello world", 5)]
    [TestCase("Hello world", 6)]
    [TestCase("Hello world", 10)]
    [TestCase("Hello world", 100)]
    [TestCase("The quick brown fox jumped over the lazy dog", 12)]
    public async Task ReadAsync_VariableReadSize_Success(string message, int readSize)
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes(message);

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(Convert.ToBase64String(data)));
        var base64Stream = new Base64ResponseStream(ms);

        // Act
        var allData = new List<byte>();
        var buffer = new byte[readSize];

        int read;
        while ((read = await ReadAsync(base64Stream, buffer)) > 0)
        {
            allData.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        var readData = allData.ToArray();

        // Assert
        CollectionAssert.AreEqual(data, readData);
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
