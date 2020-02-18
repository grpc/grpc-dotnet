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
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Gateway.Testing;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client
{
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
    public class ServerStreamingMethodTests : GrpcWebFunctionalTestBase
    {
        public ServerStreamingMethodTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
         : base(grpcTestMode, endpointName)
        {
        }

        [Test]
        public async Task SendValidRequest_StreamedContentReturned()
        {
            // Arrage
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new EchoService.EchoServiceClient(channel);

            // Act
            var call = client.ServerStreamingEcho(new ServerStreamingEchoRequest
            {
                Message = "test",
                MessageCount = 3,
                MessageInterval = TimeSpan.FromMilliseconds(10).ToDuration()
            });

            // Assert
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual("test", call.ResponseStream.Current.Message);

            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual("test", call.ResponseStream.Current.Message);

            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual("test", call.ResponseStream.Current.Message);

            Assert.IsFalse(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual(null, call.ResponseStream.Current);

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task SendValidRequest_ServerAbort_ClientThrowsAbortException()
        {
            // Arrage
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new EchoService.EchoServiceClient(channel);

            // Act
            var call = client.ServerStreamingEchoAbort(new ServerStreamingEchoRequest
            {
                Message = "test",
                MessageCount = 3,
                MessageInterval = TimeSpan.FromMilliseconds(10).ToDuration()
            });

            // Assert
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual("test", call.ResponseStream.Current.Message);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
            Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);
            Assert.AreEqual("Aborted from server side.", ex.Status.Detail);

            Assert.AreEqual(StatusCode.Aborted, call.GetStatus().StatusCode);

            AssertHasLogRpcConnectionError(StatusCode.Aborted, "Aborted from server side.");
        }

        [Test]
        public async Task SendValidRequest_ClientAbort_ClientThrowsCancelledException()
        {
            // Arrage
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var cts = new CancellationTokenSource();
            var client = new EchoService.EchoServiceClient(channel);

            // Act
            var call = client.ServerStreamingEcho(new ServerStreamingEchoRequest
            {
                Message = "test",
                MessageCount = 2,
                MessageInterval = TimeSpan.FromMilliseconds(100).ToDuration()
            }, cancellationToken: cts.Token);

            // Assert
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.AreEqual("test", call.ResponseStream.Current.Message);

            cts.Cancel();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("Call canceled by the client.", ex.Status.Detail);

            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);

            AssertHasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'Call canceled by the client.'.");
        }
    }
}
