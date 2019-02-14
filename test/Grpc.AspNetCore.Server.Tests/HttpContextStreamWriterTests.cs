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
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class HttpContextStreamWriterTests
    {
        private static readonly GrpcServiceOptions TestServiceOptions = new GrpcServiceOptions();

        [Test]
        public async Task WriteAsync_DefaultWriteOptions_Flushes()
        {
            // Arrange
            var ms = new MemoryStream();

            var httpContext = new DefaultHttpContext();
            httpContext.Response.BodyPipe = new StreamPipeWriter(ms);
            var serverCallContext = new HttpContextServerCallContext(httpContext, NullLogger.Instance);
            var writer = new HttpContextStreamWriter<HelloReply>(serverCallContext, TestServiceOptions, (message) => message.ToByteArray());

            // Act 1
            await writer.WriteAsync(new HelloReply
            {
                Message = "Hello world 1"
            });

            // Assert 1
            Assert.AreEqual(20, ms.Length);

            // Act 2
            await writer.WriteAsync(new HelloReply
            {
                Message = "Hello world 2"
            });

            // Assert 2
            Assert.AreEqual(40, ms.Length);

            ms.Seek(0, SeekOrigin.Begin);
            var pipeReader = new StreamPipeReader(ms);

            var writtenMessage1 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.AreEqual("Hello world 1", writtenMessage1.Message);
            var writtenMessage2 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.AreEqual("Hello world 2", writtenMessage2.Message);
        }

        [Test]
        public async Task WriteAsync_BufferHintWriteOptions_DoesNotFlush()
        {
            // Arrange
            var ms = new MemoryStream();

            var httpContext = new DefaultHttpContext();
            httpContext.Response.BodyPipe = new StreamPipeWriter(ms);
            var serverCallContext = new HttpContextServerCallContext(httpContext, NullLogger.Instance);
            var writer = new HttpContextStreamWriter<HelloReply>(serverCallContext, TestServiceOptions, (message) => message.ToByteArray());
            serverCallContext.WriteOptions = new WriteOptions(WriteFlags.BufferHint);

            // Act 1 
            await writer.WriteAsync(new HelloReply
            {
                Message = "Hello world 1"
            });

            // Assert 1
            Assert.AreEqual(0, ms.Length);

            // Act 2
            await writer.WriteAsync(new HelloReply
            {
                Message = "Hello world 2"
            });

            // Assert 2
            Assert.AreEqual(0, ms.Length);

            await httpContext.Response.BodyPipe.FlushAsync();

            ms.Seek(0, SeekOrigin.Begin);
            var pipeReader = new StreamPipeReader(ms);

            var writtenMessage1 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.AreEqual("Hello world 1", writtenMessage1.Message);
            var writtenMessage2 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.AreEqual("Hello world 2", writtenMessage2.Message);
        }
    }
}
