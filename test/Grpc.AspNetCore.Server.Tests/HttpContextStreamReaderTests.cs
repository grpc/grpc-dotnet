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
using Greet;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

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
        var reader = new HttpContextStreamReader<HelloReply>(serverCallContext, MessageHelpers.ServiceMethod.ResponseMarshaller.ContextualDeserializer);

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

        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IRequestBodyPipeFeature>(new TestRequestBodyPipeFeature(PipeReader.Create(ms)));
        var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext, logger: testLoggerFactory.CreateLogger("Test"));
        var reader = new HttpContextStreamReader<HelloReply>(serverCallContext, MessageHelpers.ServiceMethod.ResponseMarshaller.ContextualDeserializer);

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
        catch (OperationCanceledException)
        {
        }

        Assert.IsTrue(nextTask.IsCompleted);
        Assert.IsTrue(nextTask.IsCanceled);

        Assert.AreEqual(1, testSink.Writes.Count);
        Assert.AreEqual("ReadingMessage", testSink.Writes.First().EventId.Name);
    }

    [Test]
    public async Task MoveNext_MultipleCalls_CurrentClearedBetweenCalls()
    {
        // Arrange
        var ms = new SyncPointMemoryStream();

        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IRequestBodyPipeFeature>(new TestRequestBodyPipeFeature(PipeReader.Create(ms)));
        var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext, logger: testLoggerFactory.CreateLogger("Test"));
        var reader = new HttpContextStreamReader<HelloReply>(serverCallContext, MessageHelpers.ServiceMethod.ResponseMarshaller.ContextualDeserializer);

        // Act
        var nextTask = reader.MoveNext(CancellationToken.None);

        await ms.AddDataAndWait(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x00 // length = 0
            }).DefaultTimeout();

        Assert.IsTrue(await nextTask.DefaultTimeout());
        Assert.IsNotNull(reader.Current);

        nextTask = reader.MoveNext(CancellationToken.None);

        Assert.IsFalse(nextTask.IsCompleted);
        Assert.IsFalse(nextTask.IsCanceled);

        // Assert
        Assert.IsNull(reader.Current);
    }
}
