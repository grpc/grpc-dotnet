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
using System.IO.Pipelines;
using System.Text;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public abstract class PipeExtensionsTestsBase
    {
        protected abstract Marshaller<TestData> TestDataMarshaller { get; }

        [Test]
        public async Task ReadSingleMessageAsync_EmptyMessage_ReturnNoData()
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

            var pipeReader = new TestPipeReader(PipeReader.Create(ms));

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer);

            // Assert
            Assert.AreEqual(0, messageData.Span.Length);
            Assert.AreEqual(5, pipeReader.Consumed);
        }

        [Test]
        public async Task ReadSingleMessageAsync_OneByteMessage_ReturnData()
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer);

            // Assert
            Assert.AreEqual(1, messageData.Span.Length);
            Assert.AreEqual(0x10, messageData.Span[0]);
        }

        [Test]
        public async Task ReadSingleMessageAsync_UnderReceiveSize_ReturnData()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxSendMessageSize: 1);
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                });

            var pipeReader = PipeReader.Create(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(context, TestDataMarshaller.ContextualDeserializer);

            // Assert
            Assert.AreEqual(1, messageData.Span.Length);
            Assert.AreEqual(0x10, messageData.Span[0]);
        }

        [Test]
        public async Task ReadSingleMessageAsync_ExceedReceiveSize_ReturnData()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxReceiveMessageSize: 1);
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pipeReader.ReadSingleMessageAsync(context, TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
        }

        [Test]
        public async Task ReadSingleMessageAsync_LongMessage_ReturnData()
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer);

            // Assert
            Assert.AreEqual(449, messageData.Span.Length);
            CollectionAssert.AreEqual(content, messageData.Span.ToArray());
        }

        [Test]
        public async Task ReadStreamMessageAsync_LongMessage_ReturnData()
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer);

            // Assert
            Assert.AreEqual(449, messageData!.Span.Length);
            CollectionAssert.AreEqual(content, messageData.Span.ToArray());
        }

        [Test]
        public async Task ReadStreamMessageAsync_MultipleEmptyMessages_ReturnNoDataMessageThenComplete()
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

            var pipeReader = new TestPipeReader(PipeReader.Create(ms));
            var testServerCallContext = HttpContextServerCallContextHelper.CreateServerCallContext();

            // Act 1
            var messageData1 = await pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer);

            // Assert 1
            Assert.AreEqual(0, messageData1!.Span.Length);
            Assert.AreEqual(emptyMessage.Length, pipeReader.Consumed);

            // Act 2
            var messageData2 = await pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer);

            // Assert 2
            Assert.AreEqual(0, messageData2!.Span.Length);
            Assert.AreEqual(emptyMessage.Length * 2, pipeReader.Consumed);

            // Act 3
            var messageData3 = await pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer);

            // Assert 3
            Assert.IsNull(messageData3);
            Assert.AreEqual(emptyMessage.Length * 2, pipeReader.Consumed);
        }

        [Test]
        public async Task ReadStreamMessageAsync_MessageSplitAcrossReadsWithAdditionalData_ExamineMessageOnly()
        {
            // Arrange
            var emptyMessage = new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00, // length = 0
                    0x00, // compression = 0
                };
            var followingMessage = new byte[]
                {
                    0x00,
                    0x00,
                    0x00,
                    0x00, // length = 0
                    0x00, // extra data
                };

            var requestStream = new SyncPointMemoryStream(runContinuationsAsynchronously: false);

            var pipeReader = new TestPipeReader(PipeReader.Create(requestStream));
            var testServerCallContext = HttpContextServerCallContextHelper.CreateServerCallContext();

            // Act 1
            var messageData1Task = pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer).AsTask();
            await requestStream.AddDataAndWait(emptyMessage).DefaultTimeout();

            // Assert 1
            Assert.AreEqual(0, (await messageData1Task.DefaultTimeout())!.Span.Length);
            Assert.AreEqual(5, pipeReader.Consumed);
            Assert.AreEqual(5, pipeReader.Examined);

            // Act 2
            var messageData2Task = pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer).AsTask();
            await requestStream.AddDataAndWait(followingMessage).DefaultTimeout();

            // Assert 2
            Assert.AreEqual(0, (await messageData2Task.DefaultTimeout())!.Span.Length);
            Assert.AreEqual(10, pipeReader.Consumed);
            Assert.AreEqual(10, pipeReader.Examined);

            // Act 3
            var messageData3Task = pipeReader.ReadStreamMessageAsync(testServerCallContext, TestDataMarshaller.ContextualDeserializer).AsTask();
            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            // Assert 3
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => messageData3Task).DefaultTimeout();
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(10, pipeReader.Consumed);
            Assert.AreEqual(11, pipeReader.Examined); // Examined ahead to ask for more data
        }

        [Test]
        public async Task ReadSingleMessageAsync_HeaderIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00
                });

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
                () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task ReadSingleMessageAsync_MessageDataIncomplete_ThrowError()
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
                () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task ReadSingleMessageAsync_AdditionalData_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10,
                    0x10 // additional data
                });

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
                () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Additional data after the message received.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task ReadSingleMessageAsync_AdditionalDataInSeparatePipeRead_ThrowError()
        {
            // Arrange
            var requestStream = new SyncPointMemoryStream();

            var pipeReader = PipeReader.Create(requestStream);

            // Act
            var readTask = pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask();

            // Assert
            Assert.IsFalse(readTask.IsCompleted, "Still waiting for data");

            await requestStream.AddDataAndWait(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                }).DefaultTimeout();

            Assert.IsFalse(readTask.IsCompleted, "Still waiting for data");

            await requestStream.AddDataAndWait(new byte[] { 0x00 }).DefaultTimeout();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => readTask).DefaultTimeout();

            // Assert
            Assert.AreEqual("Additional data after the message received.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task ReadSingleMessageAsync_MessageInMultiplePipeReads_ReadMessageData()
        {
            // Arrange
            var messageData = new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                };

            // Run continuations without async so ReadSingleMessageAsync immediately consumes added data
            var requestStream = new SyncPointMemoryStream(runContinuationsAsynchronously: false);

            var pipeReader = new TestPipeReader(PipeReader.Create(requestStream));

            // Act
            var readTask = pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask();

            // Assert
            for (var i = 0; i < messageData.Length; i++)
            {
                var b = messageData[i];
                var isLast = i == messageData.Length - 1;

                Assert.IsFalse(readTask.IsCompleted, "Still waiting for data");

                await requestStream.AddDataAndWait(new[] { b }).DefaultTimeout();

                if (!isLast)
                {
                    Assert.AreEqual(0, pipeReader.Consumed);
                    Assert.AreEqual(i + 1, pipeReader.Examined);
                }
                else
                {
                    Assert.AreEqual(messageData.Length, pipeReader.Consumed); // Consumed message
                    Assert.AreEqual(messageData.Length, pipeReader.Examined);
                }
            }

            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var readMessageData = await readTask.DefaultTimeout();

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x10 }, readMessageData.Span.ToArray());
        }

        [Test]
        public async Task ReadMessageStreamAsync_HeaderIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00
                });

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
                () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task ReadStreamMessageAsync_MessageDataIncomplete_ThrowError()
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

            var pipeReader = PipeReader.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
                () => pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task WriteSingleMessageAsync_NoFlush_WriteNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteSingleMessageAsync(new TestData(Encoding.UTF8.GetBytes("Hello world")), HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualSerializer);

            // Assert
            var messageData = ms.ToArray();
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task WriteSingleMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteSingleMessageAsync(new TestData(Array.Empty<byte>()), HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualSerializer);
            await pipeWriter.FlushAsync();

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
        public async Task WriteStreamedMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(Array.Empty<byte>()), HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualSerializer);

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
        public async Task WriteStreamedMessageAsync_OneByteMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10 }), HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualSerializer);

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
        public async Task WriteStreamedMessageAsync_LongMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(content), HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualSerializer);

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
        public async Task WriteStreamedMessageAsync_MultipleOneByteMessages_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);
            var context = HttpContextServerCallContextHelper.CreateServerCallContext();

            // Act 1
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10 }), context, TestDataMarshaller.ContextualSerializer);

            // Assert 1
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

            ms.Seek(0, SeekOrigin.Begin);

            // Act 2
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x20 }), context, TestDataMarshaller.ContextualSerializer);

            // Assert 2
            messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x20
                },
                messageData);
        }

        [Test]
        public async Task WriteStreamedMessageAsync_UnderSendSize_WriteData()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxSendMessageSize: 1);
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10 }), context, TestDataMarshaller.ContextualSerializer);

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
        public async Task WriteStreamedMessageAsync_ExceedSendSize_ThrowError()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxSendMessageSize: 1);
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10, 0x10 }), context, TestDataMarshaller.ContextualSerializer)).DefaultTimeout();

            // Assert
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
        }

        [Test]
        public async Task WriteStreamedMessageAsync_GzipCompressed_WriteCompressedData()
        {
            // Arrange
            var compressionProviders = new List<ICompressionProvider>
            {
                new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest)
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "gzip";

            var context = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext,
                responseCompressionAlgorithm: "gzip",
                compressionProviders: compressionProviders);
            context.Initialize();

            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10 }), context, TestDataMarshaller.ContextualSerializer);

            // Assert
            var messageData = ms.ToArray();

            Assert.AreEqual(1, messageData[0]); // compression
            Assert.AreEqual(21, messageData[4]); // message length

            byte[] result = Decompress(compressionProviders.Single(), messageData);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(0x10, result[0]);
        }

        [Test]
        public async Task WriteStreamedMessageAsync_HasCustomCompressionLevel_WriteCompressedDataWithLevel()
        {
            // Arrange
            var mockCompressionProvider = new MockCompressionProvider();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "Mock";

            var context = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext,
                responseCompressionAlgorithm: "Mock",
                responseCompressionLevel: System.IO.Compression.CompressionLevel.Optimal,
                compressionProviders: new List<ICompressionProvider>
                {
                    mockCompressionProvider
                });
            context.Initialize();

            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteStreamedMessageAsync(new TestData(new byte[] { 0x10 }), context, TestDataMarshaller.ContextualSerializer);

            // Assert
            Assert.AreEqual(System.IO.Compression.CompressionLevel.Optimal, mockCompressionProvider.ArgumentCompression);

            var messageData = ms.ToArray();

            Assert.AreEqual(1, messageData[0]); // compression
            Assert.AreEqual(21, messageData[4]); // message length

            byte[] result = Decompress(mockCompressionProvider, messageData);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(0x10, result[0]);
        }

        private static byte[] Decompress(ICompressionProvider compressionProvider, byte[] messageData)
        {
            var output = new MemoryStream();

            var content = new MemoryStream(messageData.AsMemory(5).ToArray());
            var decompressionStream = compressionProvider.CreateDecompressionStream(content);
            decompressionStream.CopyTo(output);

            var result = output.ToArray();
            return result;
        }

        public class MockCompressionProvider : ICompressionProvider
        {
            private readonly GzipCompressionProvider _inner = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Optimal);

            public string EncodingName => "Mock";
            public System.IO.Compression.CompressionLevel? ArgumentCompression { get; set; }

            public Stream CreateCompressionStream(Stream stream, System.IO.Compression.CompressionLevel? compressionLevel)
            {
                ArgumentCompression = compressionLevel;
                return _inner.CreateCompressionStream(stream, compressionLevel);
            }

            public Stream CreateDecompressionStream(Stream stream)
            {
                return _inner.CreateDecompressionStream(stream);
            }
        }
    }
}
