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

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.Server.Compression;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class PipeExtensionsTests
    {
        private static readonly HttpContextServerCallContext TestServerCallContext = HttpContextServerCallContextHelper.CreateServerCallContext();

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
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServerCallContext);

            // Assert
            Assert.AreEqual(0, messageData.Length);
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
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServerCallContext);

            // Assert
            Assert.AreEqual(1, messageData.Length);
            Assert.AreEqual(0x10, messageData[0]);
        }

        [Test]
        public async Task ReadSingleMessageAsync_UnderReceiveSize_ReturnData()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(serviceOptions: new GrpcServiceOptions { SendMaxMessageSize = 1 });
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
            var messageData = await pipeReader.ReadSingleMessageAsync(context);

            // Assert
            Assert.AreEqual(1, messageData.Length);
            Assert.AreEqual(0x10, messageData[0]);
        }

        [Test]
        public async Task ReadSingleMessageAsync_ExceedReceiveSize_ReturnData()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(serviceOptions: new GrpcServiceOptions { ReceiveMaxMessageSize = 1 });
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
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pipeReader.ReadSingleMessageAsync(context).AsTask()).DefaultTimeout();

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
            var messageData = await pipeReader.ReadSingleMessageAsync(TestServerCallContext);

            // Assert
            Assert.AreEqual(449, messageData.Length);
            CollectionAssert.AreEqual(content, messageData);
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
            var messageData = await pipeReader.ReadStreamMessageAsync(TestServerCallContext);

            // Assert
            Assert.AreEqual(449, messageData!.Length);
            CollectionAssert.AreEqual(content, messageData);
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

            // Act 1
            var messageData1 = await pipeReader.ReadStreamMessageAsync(TestServerCallContext);

            // Assert 1
            Assert.AreEqual(0, messageData1!.Length);
            Assert.AreEqual(emptyMessage.Length, pipeReader.Consumed);

            // Act 2
            var messageData2 = await pipeReader.ReadStreamMessageAsync(TestServerCallContext);

            // Assert 2
            Assert.AreEqual(0, messageData2!.Length);
            Assert.AreEqual(emptyMessage.Length * 2, pipeReader.Consumed);

            // Act 3
            var messageData3 = await pipeReader.ReadStreamMessageAsync(TestServerCallContext);

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

            // Act 1
            var messageData1Task = pipeReader.ReadStreamMessageAsync(TestServerCallContext).AsTask();
            await requestStream.AddDataAndWait(emptyMessage).DefaultTimeout();

            // Assert 1
            Assert.AreEqual(0, (await messageData1Task.DefaultTimeout())!.Length);
            Assert.AreEqual(5, pipeReader.Consumed);
            Assert.AreEqual(5, pipeReader.Examined);

            // Act 2
            var messageData2Task = pipeReader.ReadStreamMessageAsync(TestServerCallContext).AsTask();
            await requestStream.AddDataAndWait(followingMessage).DefaultTimeout();

            // Assert 2
            Assert.AreEqual(0, (await messageData2Task.DefaultTimeout())!.Length);
            Assert.AreEqual(10, pipeReader.Consumed);
            Assert.AreEqual(10, pipeReader.Examined);

            // Act 3
            var messageData3Task = pipeReader.ReadStreamMessageAsync(TestServerCallContext).AsTask();
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
                () => pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask()).DefaultTimeout();

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
                () => pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask()).DefaultTimeout();

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
                () => pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask()).DefaultTimeout();

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
            var readTask = pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask();

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
            var readTask = pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask();

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
            CollectionAssert.AreEqual(new byte[] { 0x10 }, readMessageData);
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
                () => pipeReader.ReadSingleMessageAsync(TestServerCallContext).AsTask()).DefaultTimeout();

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
                () => pipeReader.ReadStreamMessageAsync(TestServerCallContext).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        }

        [Test]
        public async Task WriteMessageAsync_NoFlush_WriteNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Encoding.UTF8.GetBytes("Hello world"), TestServerCallContext);

            // Assert
            var messageData = ms.ToArray();
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task WriteMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Array.Empty<byte>(), TestServerCallContext, flush: true);

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
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, TestServerCallContext, flush: true);

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
            var pipeWriter = PipeWriter.Create(ms);
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            // Act
            await pipeWriter.WriteMessageAsync(content, TestServerCallContext, flush: true);

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
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(serviceOptions: new GrpcServiceOptions { SendMaxMessageSize = 1 });
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, context, flush: true);

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
        public async Task WriteMessageAsync_ExceedSendSize_ThrowError()
        {
            // Arrange
            var context = HttpContextServerCallContextHelper.CreateServerCallContext(serviceOptions: new GrpcServiceOptions { SendMaxMessageSize = 1 });
            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pipeWriter.WriteMessageAsync(new byte[] { 0x10, 0x10 }, context, flush: true)).DefaultTimeout();

            // Assert
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
        }

        [Test]
        public async Task WriteMessageAsync_GzipCompressed_WriteCompressedData()
        {
            // Arrange
            var serviceOptions = new GrpcServiceOptions
            {
                ResponseCompressionAlgorithm = "gzip",
                CompressionProviders =
                {
                    new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest)
                }
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "gzip";

            var context = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext, serviceOptions);
            context.Initialize();

            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, context, flush: true);

            // Assert
            var messageData = ms.ToArray();

            Assert.AreEqual(1, messageData[0]); // compression
            Assert.AreEqual(21, messageData[4]); // message length
        }

        [Test]
        public async Task WriteMessageAsync_HasCustomCompressionLevel_WriteCompressedDataWithLevel()
        {
            // Arrange
            var mockCompressionProvider = new MockCompressionProvider();
            var serviceOptions = new GrpcServiceOptions
            {
                ResponseCompressionAlgorithm = "Mock",
                ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Optimal,
                CompressionProviders =
                {
                    mockCompressionProvider
                }
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = "Mock";

            var context = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext, serviceOptions);
            context.Initialize();

            var ms = new MemoryStream();
            var pipeWriter = PipeWriter.Create(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, context, flush: true);

            // Assert
            Assert.AreEqual(System.IO.Compression.CompressionLevel.Optimal, mockCompressionProvider.ArgumentCompression);

            var messageData = ms.ToArray();

            Assert.AreEqual(1, messageData[0]); // compression
            Assert.AreEqual(21, messageData[4]); // message length
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

        public class TestPipeReader : PipeReader
        {
            private readonly PipeReader _pipeReader;
            private ReadOnlySequence<byte> _currentBuffer;

            public long Consumed { get; set; }
            public long Examined { get; set; }

            public TestPipeReader(PipeReader pipeReader)
            {
                _pipeReader = pipeReader;
            }

            public override void AdvanceTo(SequencePosition consumed)
            {
                Consumed += _currentBuffer.Slice(0, consumed).Length;
                Examined = Consumed;
                _pipeReader.AdvanceTo(consumed);
            }

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
            {
                Consumed += _currentBuffer.Slice(0, consumed).Length;
                Examined = Consumed + _currentBuffer.Slice(0, examined).Length;
                _pipeReader.AdvanceTo(consumed, examined);
            }

            public override void CancelPendingRead()
            {
                _pipeReader.CancelPendingRead();
            }

            public override void Complete(Exception? exception = null)
            {
                _pipeReader.Complete(exception);
            }

            public override void OnWriterCompleted(Action<Exception, object> callback, object state)
            {
                _pipeReader.OnWriterCompleted(callback, state);
            }

            public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken);
                _currentBuffer = result.Buffer;

                return result;
            }

            public override bool TryRead(out ReadResult result)
            {
                return _pipeReader.TryRead(out result);
            }
        }
    }
}
