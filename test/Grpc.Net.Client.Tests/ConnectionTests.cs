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
using System.Net.Sockets;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class ConnectionTests
{
    [Test]
    public async Task UnaryCall_Http1Response_ThrowError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, version: new Version(1, 1));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(1)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Bad gRPC response. Response protocol downgraded to HTTP/1.1.", ex.Status.Detail);
    }

    [TestCase(SocketError.HostNotFound, StatusCode.Unavailable)]
    [TestCase(SocketError.ConnectionRefused, StatusCode.Unavailable)]
    public async Task UnaryCall_SocketException_ThrowCorrectStatus(SocketError socketError, StatusCode statusCode)
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Blah", new SocketException((int)socketError)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(1)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        Assert.AreEqual(statusCode, ex.StatusCode);
    }

    [Test]
    public async Task UnaryCall_IOException_ThrowCorrectStatus()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Blah", new IOException("")));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(1)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
    }
}
