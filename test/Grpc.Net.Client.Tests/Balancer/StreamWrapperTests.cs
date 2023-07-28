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

#if SUPPORT_LOAD_BALANCING
using Grpc.Net.Client.Balancer.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class StreamWrapperTests
{
    [Test]
    public async Task ReadAsync_ExactSize_Read()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 4 });
        var data = new List<ReadOnlyMemory<byte>>
        {
            new byte[] { 1, 2, 3 }
        };
        var streamWrapper = new StreamWrapper(ms, s => { }, data);
        var buffer = new byte[3];

        // Act & Assert
        Assert.AreEqual(3, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(1, buffer[0]);
        Assert.AreEqual(2, buffer[1]);
        Assert.AreEqual(3, buffer[2]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(4, buffer[0]);

        Assert.AreEqual(0, await streamWrapper.ReadAsync(buffer));
    }

    [Test]
    public async Task ReadAsync_BiggerThanNeeded_Read()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 4 });
        var data = new List<ReadOnlyMemory<byte>>
        {
            new byte[] { 1, 2, 3 }
        };
        var streamWrapper = new StreamWrapper(ms, s => { }, data);
        var buffer = new byte[4];

        // Act & Assert
        Assert.AreEqual(3, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(1, buffer[0]);
        Assert.AreEqual(2, buffer[1]);
        Assert.AreEqual(3, buffer[2]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(4, buffer[0]);

        Assert.AreEqual(0, await streamWrapper.ReadAsync(buffer));
    }

    [Test]
    public async Task ReadAsync_MultipleInitialData_ReadInOrder()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 4 });
        var data = new List<ReadOnlyMemory<byte>>
        {
            new byte[] { 1 },
            new byte[] { 2 },
            new byte[] { 3 },
        };
        var streamWrapper = new StreamWrapper(ms, s => { }, data);
        var buffer = new byte[1024];

        // Act & Assert
        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(1, buffer[0]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(2, buffer[0]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(3, buffer[0]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(4, buffer[0]);

        Assert.AreEqual(0, await streamWrapper.ReadAsync(buffer));
    }

    [Test]
    public async Task ReadAsync_BufferSmallerThanInitialData_ReadInOrder()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 6 });
        var data = new List<ReadOnlyMemory<byte>>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5 }
        };
        var streamWrapper = new StreamWrapper(ms, s => { }, data);
        var buffer = new byte[2];

        // Act & Assert
        Assert.AreEqual(2, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(1, buffer[0]);
        Assert.AreEqual(2, buffer[1]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(3, buffer[0]);

        Assert.AreEqual(2, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(4, buffer[0]);
        Assert.AreEqual(5, buffer[1]);

        Assert.AreEqual(1, await streamWrapper.ReadAsync(buffer));
        Assert.AreEqual(6, buffer[0]);

        Assert.AreEqual(0, await streamWrapper.ReadAsync(buffer));
    }
}

#endif
