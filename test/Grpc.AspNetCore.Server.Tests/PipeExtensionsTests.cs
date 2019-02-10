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
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class PipeExtensionsTests
    {
        private static readonly GrpcServiceOptions TestServiceOptions = new GrpcServiceOptions();

        [Test]
        public async Task ReadMessageAsync_EmptyMessage_ReturnNoData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00 // length = 0
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServiceOptions);

            // Assert
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task ReadMessageAsync_OneByteMessage_ReturnData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServiceOptions);

            // Assert
            Assert.AreEqual(1, messageData.Length);
            Assert.AreEqual(0x10, messageData[0]);
        }

        [Test]
        public async Task ReadMessageAsync_UnderReceiveSize_ReturnData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(new GrpcServiceOptions { SendMaxMessageSize = 1 });

            // Assert
            Assert.AreEqual(1, messageData.Length);
            Assert.AreEqual(0x10, messageData[0]);
        }

        [Test]
        public void ReadMessageAsync_ExceedReceiveSize_ReturnData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x02, // length = 1
                    0x10,
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(() => pipeReader.ReadSingleMessageAsync(new GrpcServiceOptions { ReceiveMaxMessageSize = 1 }).AsTask());

            // Assert
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Message);
        }

        [Test]
        public async Task ReadMessageAsync_LongMessage_ReturnData()
        {
            // Arrange
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x01,
                    0xC1 // length = 449
                }.Concat(content).ToArray());

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServiceOptions);

            // Assert
            Assert.AreEqual(449, messageData.Length);
            CollectionAssert.AreEqual(content, messageData);
        }

        [Test]
        public async Task ReadMessageStreamAsync_LongMessage_ReturnData()
        {
            // Arrange
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x01,
                    0xC1 // length = 449
                }.Concat(content).ToArray());

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadStreamMessageAsync(TestServiceOptions);

            // Assert
            Assert.AreEqual(449, messageData.Length);
            CollectionAssert.AreEqual(content, messageData);
        }

        [Test]
        public async Task ReadMessageStreamAsync_MultipleEmptyMessage_ReturnNoDataMessageThenComplete()
        {
            // Arrange
            var emptyMessage = new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00 // length = 0
                };
            var ms = new MemoryStream(emptyMessage.Concat(emptyMessage).ToArray());

            var pipeReader = new StreamPipeReader(ms);

            // Act 1
            var messageData1 = await pipeReader.ReadStreamMessageAsync(TestServiceOptions);

            // Assert 1
            Assert.AreEqual(0, messageData1.Length);

            // Act 2
            var messageData2 = await pipeReader.ReadStreamMessageAsync(TestServiceOptions);

            // Assert 2
            Assert.AreEqual(0, messageData2.Length);

            // Act 3
            var messageData3 = await pipeReader.ReadStreamMessageAsync(TestServiceOptions);

            // Assert 3
            Assert.IsNull(messageData3);
        }

        [Test]
        public void ReadMessageAsync_HeaderIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(
                () => pipeReader.ReadSingleMessageAsync(TestServiceOptions).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public void ReadMessageAsync_MessageDataIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x02, // length = 2
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(
                () => pipeReader.ReadSingleMessageAsync(TestServiceOptions).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public void ReadMessageAsync_AdditionalData_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 2
                    0x10,
                    0x10 // additional data
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(
                () => pipeReader.ReadSingleMessageAsync(TestServiceOptions).AsTask());

            // Assert
            Assert.AreEqual("Additional data after the message received.", ex.Message);
        }

        [Test]
        public void ReadMessageStreamAsync_HeaderIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(
                () => pipeReader.ReadSingleMessageAsync(TestServiceOptions).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public void ReadMessageStreamAsync_MessageDataIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x02, // length = 2
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(
                () => pipeReader.ReadStreamMessageAsync(TestServiceOptions).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public async Task WriteMessageAsync_NoFlush_WriteNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Encoding.UTF8.GetBytes("Hello world"), TestServiceOptions);

            // Assert
            var messageData = ms.ToArray();
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task WriteMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Array.Empty<byte>(), TestServiceOptions, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00, // length = 0
                },
                messageData);
        }

        [Test]
        public async Task WriteMessageAsync_OneByteMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, TestServiceOptions, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                },
                messageData);
        }

        [Test]
        public async Task WriteMessageAsync_LongMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            // Act
            await pipeWriter.WriteMessageAsync(content, TestServiceOptions, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x01,
                    0xC1, // length = 449
                }.Concat(content).ToArray(),
                messageData);
        }

        [Test]
        public async Task WriteMessageAsync_UnderSendSize_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, new GrpcServiceOptions { SendMaxMessageSize = 1 }, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                },
                messageData);
        }

        [Test]
        public void WriteMessageAsync_ExceedSendSize_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => pipeWriter.WriteMessageAsync(new byte[] { 0x10, 0x10 }, new GrpcServiceOptions { SendMaxMessageSize = 1 }, flush: true));

            // Assert
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Message);
        }
    }
}
