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

using System.IO.Pipelines;
using System.Text;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Web.Internal;
using Grpc.Core;
using Grpc.Gateway.Testing;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class PipeExtensionsBase64Tests
    {
        private static readonly Marshaller<EchoRequest> MarshallerEchoRequest = Marshallers.Create(arg => arg.ToByteArray(), EchoRequest.Parser.ParseFrom);
        private static readonly Marshaller<EchoResponse> MarshallerEchoResponse = Marshallers.Create(arg => arg.ToByteArray(), EchoResponse.Parser.ParseFrom);

        [Test]
        public async Task ReadSingleMessageAsync_EmptyMessage_ReturnNoData()
        {
            // Arrange
            var base64 = Convert.ToBase64String(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00, // length = 0
                });

            var data = Encoding.UTF8.GetBytes(base64);
            var ms = new MemoryStream(data);

            var pipeReader = new TestPipeReader(new Base64PipeReader(PipeReader.Create(ms)));

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualDeserializer);

            // Assert
            Assert.AreEqual(string.Empty, messageData.Message);
            Assert.AreEqual(5, pipeReader.Consumed);
        }

        [Test]
        public async Task ReadSingleMessageAsync_SmallMessage_Success()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=");
            var ms = new MemoryStream(data);

            var pipeReader = new TestPipeReader(new Base64PipeReader(PipeReader.Create(ms)));

            // Act
            var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualDeserializer);

            // Assert
            Assert.AreEqual("test", messageData.Message);
            Assert.AreEqual(11, pipeReader.Consumed);
        }

        [Test]
        public async Task ReadStreamMessageAsync_MultipleMessages_Success()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=");
            var ms = new MemoryStream(data);

            var pipeReader = new TestPipeReader(new Base64PipeReader(PipeReader.Create(ms)));

            // Act 1
            var messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 1
            Assert.AreEqual("test", messageData!.Message);
            Assert.AreEqual(11, pipeReader.Consumed);

            // Act 2
            messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 2
            Assert.AreEqual("test", messageData!.Message);
            Assert.AreEqual(22, pipeReader.Consumed);

            // Act 3
            messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 3
            Assert.AreEqual("test", messageData!.Message);
            Assert.AreEqual(33, pipeReader.Consumed);

            // Act 4
            messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 4
            Assert.AreEqual("test", messageData!.Message);
            Assert.AreEqual(44, pipeReader.Consumed);

            // Act 5
            messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 5
            Assert.AreEqual("test", messageData!.Message);
            Assert.AreEqual(55, pipeReader.Consumed);

            // Act 6
            messageData = await pipeReader.ReadStreamMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoResponse.ContextualDeserializer);

            // Assert 6
            Assert.IsNull(messageData);
        }

        [Test]
        public async Task WriteMessageAsync_NoFlush_WriteNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new Base64PipeWriter(PipeWriter.Create(ms));

            // Act
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: false);

            // Assert
            var messageData = ms.ToArray();
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task WriteMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new Base64PipeWriter(PipeWriter.Create(ms));

            // Act
            await pipeWriter.WriteMessageAsync(new EchoRequest(), HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: true);

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            var messageData = Convert.FromBase64String(base64);

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
        public async Task WriteMessageAsync_MultipleMessagesWithFlush_WriteMessagesAsSegments()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new Base64PipeWriter(PipeWriter.Create(ms));

            // Act
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: true);
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: true);
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: true);

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            Assert.AreEqual("AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=AAAAAAYKBHRlc3Q=", base64);
        }

        [Test]
        public async Task WriteMessageAsync_MultipleMessagesNoFlush_WriteMessages()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new Base64PipeWriter(PipeWriter.Create(ms));

            // Act
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: false);
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: false);
            await pipeWriter.WriteMessageAsync(new EchoRequest { Message = "test" }, HttpContextServerCallContextHelper.CreateServerCallContext(), MarshallerEchoRequest.ContextualSerializer, canFlush: false);

            pipeWriter.Complete();

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            Assert.AreEqual("AAAAAAYKBHRlc3QAAAAABgoEdGVzdAAAAAAGCgR0ZXN0", base64);
        }
    }
}
