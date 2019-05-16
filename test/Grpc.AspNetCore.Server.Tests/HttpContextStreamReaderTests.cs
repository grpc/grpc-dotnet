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
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class HttpContextStreamReaderTests
    {
        [Test]
        public void MoveNext_AlreadyCancelledToken_CancelReturnImmediately()
        {
            // Arrange
            var ms = new SyncPointMemoryStream();

            var httpContext = new DefaultHttpContext();
            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext);
            var reader = new HttpContextStreamReader<HelloReply>(serverCallContext, (data) =>
            {
                var message = new HelloReply();
                message.MergeFrom(data);
                return message;
            });

            // Act
            var nextTask = reader.MoveNext(new CancellationToken(true));

            // Assert
            Assert.IsTrue(nextTask.IsCompleted);
            Assert.IsTrue(nextTask.IsCanceled);
        }

        [Test]
        public async Task MoveNext_TokenCancelledDuringMoveNext_CancelTask()
        {
            // Arrange
            var ms = new SyncPointMemoryStream();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.BodyReader = new StreamPipeReader(ms);
            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext);
            var reader = new HttpContextStreamReader<HelloReply>(serverCallContext, (data) =>
            {
                var message = new HelloReply();
                message.MergeFrom(data);
                return message;
            });

            var cts = new CancellationTokenSource();

            var nextTask = reader.MoveNext(cts.Token);

            Assert.IsFalse(nextTask.IsCompleted);
            Assert.IsFalse(nextTask.IsCanceled);

            cts.Cancel();

            try
            {
                await nextTask;
                Assert.Fail();
            }
            catch (TaskCanceledException)
            {
            }

            Assert.IsTrue(nextTask.IsCompleted);
            Assert.IsTrue(nextTask.IsCanceled);
        }
    }
}
