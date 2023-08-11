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
public class Base64RequestStreamTests
{
    [Test]
    public async Task WriteAsync_SmallData_Written()
    {
        // Arrange
        var ms = new MemoryStream();
        var gprcWebStream = new Base64RequestStream(ms);

        var data = Encoding.UTF8.GetBytes("123");

        // Act
        await WriteAsync(gprcWebStream, data).DefaultTimeout();

        // Assert
        var base64 = Encoding.UTF8.GetString(ms.ToArray());
        CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));
    }

    [Test]
    public async Task WriteAsync_VeryLargeData_Written()
    {
        // Arrange
        var ms = new MemoryStream();
        var gprcWebStream = new Base64RequestStream(ms);

        var chars = new char[16384];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Convert.ToChar(i % 10);
        }

        var data = Encoding.UTF8.GetBytes(new string(chars));

        // Act
        await WriteAsync(gprcWebStream, data).DefaultTimeout();
        await gprcWebStream.FlushAsync().DefaultTimeout();

        // Assert
        var base64 = Encoding.UTF8.GetString(ms.ToArray());
        var result = Convert.FromBase64String(base64);
        CollectionAssert.AreEqual(data, result);
    }

    [Test]
    public async Task WriteAsync_MultipleSingleBytes_Written()
    {
        // Arrange
        var ms = new MemoryStream();
        var gprcWebStream = new Base64RequestStream(ms);

        var data = Encoding.UTF8.GetBytes("123");

        // Act
        foreach (var b in data)
        {
            await WriteAsync(gprcWebStream, new[] { b }).DefaultTimeout();
        }

        // Assert
        var base64 = Encoding.UTF8.GetString(ms.ToArray());
        CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));
    }

    [Test]
    public async Task WriteAsync_SmallDataWithRemainder_WrittenWithoutRemainder()
    {
        // Arrange
        var ms = new MemoryStream();
        var gprcWebStream = new Base64RequestStream(ms);

        var data = Encoding.UTF8.GetBytes("Hello world");

        // Act
        await WriteAsync(gprcWebStream, data).DefaultTimeout();

        // Assert
        var base64 = Encoding.UTF8.GetString(ms.ToArray());
        var newData = Convert.FromBase64String(base64);
        CollectionAssert.AreEqual(data.AsSpan(0, newData.Length).ToArray(), newData);
    }

    [Test]
    public async Task FlushAsync_HasRemainder_WriteRemainder()
    {
        // Arrange
        var ms = new MemoryStream();
        var gprcWebStream = new Base64RequestStream(ms);

        var data = Encoding.UTF8.GetBytes("Hello world");

        // Act
        await WriteAsync(gprcWebStream, data).DefaultTimeout();
        await gprcWebStream.FlushAsync().DefaultTimeout();

        // Assert
        var base64 = Encoding.UTF8.GetString(ms.ToArray());
        CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));
    }

    private static Task WriteAsync(Stream stream, Memory<byte> data, CancellationToken cancellationToken = default)
    {
#if NET462
        var success = MemoryMarshal.TryGetArray<byte>(data, out var segment);
        Debug.Assert(success);
        return stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
#else
        return stream.WriteAsync(data, cancellationToken).AsTask();
#endif
    }
}
