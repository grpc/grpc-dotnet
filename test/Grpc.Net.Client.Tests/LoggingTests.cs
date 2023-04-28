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

using System.Net;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class LoggingTests
{
    [Test]
    public async Task AsyncUnaryCall_Success_LogToFactory()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            // Trigger request stream serialization
            await request.Content!.ReadAsStreamAsync().DefaultTimeout();

            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply
            {
                Message = "Hello world"
            }).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

        // Act
        var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

        // Assert
        Assert.AreEqual("Hello world", rs.Message);

        var log = testSink.Writes.Single(w => w.EventId.Name == "StartingCall");
        Assert.AreEqual("Starting gRPC call. Method type: 'Unary', URI: 'https://localhost/ServiceName/MethodName'.", log.State.ToString());
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "SendingMessage");
        Assert.AreEqual("Sending message.", log.State.ToString());
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "ReadingMessage");
        Assert.AreEqual("Reading message.", log.State.ToString());
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "FinishedCall");
        Assert.AreEqual("Finished gRPC call.", log.State.ToString());
        AssertScope(log);

        static void AssertScope(WriteContext log)
        {
            var scope = (IReadOnlyList<KeyValuePair<string, object>>)log.Scope;
            Assert.AreEqual(MethodType.Unary, scope[0].Value);
            Assert.AreEqual(new Uri("/ServiceName/MethodName", UriKind.Relative), scope[1].Value);
        }
    }

    [Test]
    public async Task AsyncUnaryCall_RequestFailure_LogToFactory()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error occurred."));
        });

        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseAsync);
        var debugException = ex.Status.DebugException;

        // Assert
        Assert.NotNull(debugException);

        var log = testSink.Writes.Single(w => w.EventId.Name == "StartingCall");
        Assert.AreEqual("Starting gRPC call. Method type: 'Unary', URI: 'https://localhost/ServiceName/MethodName'.", log.State.ToString());
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "ErrorStartingCall");
        Assert.AreEqual("Error starting gRPC call.", log.State.ToString());
        Assert.Null(log.Exception);
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "GrpcStatusError");
        Assert.AreEqual("Call failed with gRPC error status. Status code: 'Internal', Message: 'Error starting gRPC call. Exception: An error occurred.'.", log.State.ToString());
        Assert.AreEqual(debugException, log.Exception);
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "FinishedCall");
        Assert.AreEqual("Finished gRPC call.", log.State.ToString());
        AssertScope(log);

        static void AssertScope(WriteContext log)
        {
            var scope = (IReadOnlyList<KeyValuePair<string, object>>)log.Scope;
            Assert.AreEqual(MethodType.Unary, scope[0].Value);
            Assert.AreEqual(new Uri("/ServiceName/MethodName", UriKind.Relative), scope[1].Value);
        }
    }

    [Test]
    public async Task AsyncUnaryCall_CredentialsFailure_LogToFactory()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error occurred."));
        });

        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory, configure: o =>
        {
            var credentials = CallCredentials.FromInterceptor((c, m) =>
            {
                return Task.FromException(new Exception("Credentials error."));
            });
            o.Credentials = ChannelCredentials.Create(ChannelCredentials.SecureSsl, credentials);
        });

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseAsync);
        var debugException = ex.Status.DebugException;

        // Assert
        Assert.NotNull(debugException);

        var log = testSink.Writes.Single(w => w.EventId.Name == "StartingCall");
        Assert.AreEqual("Starting gRPC call. Method type: 'Unary', URI: 'https://localhost/ServiceName/MethodName'.", log.State.ToString());
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "GrpcStatusError");
        Assert.AreEqual("Call failed with gRPC error status. Status code: 'Internal', Message: 'Error starting gRPC call. Exception: Credentials error.'.", log.State.ToString());
        Assert.AreEqual(debugException, log.Exception);
        AssertScope(log);

        log = testSink.Writes.Single(w => w.EventId.Name == "FinishedCall");
        Assert.AreEqual("Finished gRPC call.", log.State.ToString());
        AssertScope(log);

        static void AssertScope(WriteContext log)
        {
            var scope = (IReadOnlyList<KeyValuePair<string, object>>)log.Scope;
            Assert.AreEqual(MethodType.Unary, scope[0].Value);
            Assert.AreEqual(new Uri("/ServiceName/MethodName", UriKind.Relative), scope[1].Value);
        }
    }
}
