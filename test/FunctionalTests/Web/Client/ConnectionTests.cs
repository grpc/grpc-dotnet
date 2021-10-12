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
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Gateway.Testing;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client
{
    public class ConnectionTests : FunctionalTestBase
    {
        private HttpClient CreateGrpcWebClient(TestServerEndpointName endpointName, Version? version)
        {
            GrpcWebHandler grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb);
            grpcWebHandler.HttpVersion = version;

            return Fixture.CreateClient(endpointName, grpcWebHandler);
        }

        private GrpcChannel CreateGrpcWebChannel(TestServerEndpointName endpointName, Version? version)
        {
            var httpClient = CreateGrpcWebClient(endpointName, version);
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            return channel;
        }

        [TestCase(TestServerEndpointName.Http1, "2.0", false)]
        [TestCase(TestServerEndpointName.Http1, "1.1", true)]
        [TestCase(TestServerEndpointName.Http1, null, false)]
        [TestCase(TestServerEndpointName.Http2, "2.0", true)]
        [TestCase(TestServerEndpointName.Http2, "1.1", false)]
        [TestCase(TestServerEndpointName.Http2, null, true)]
#if NET5_0_OR_GREATER
        // Specifing HTTP/2 doesn't work when the server is using TLS with HTTP/1.1
        // Caused by using HttpVersionPolicy.RequestVersionOrHigher setting
        [TestCase(TestServerEndpointName.Http1WithTls, "2.0", false)]
#else
        [TestCase(TestServerEndpointName.Http1WithTls, "2.0", true)]
#endif
        [TestCase(TestServerEndpointName.Http1WithTls, "1.1", true)]
        [TestCase(TestServerEndpointName.Http1WithTls, null, true)]
        [TestCase(TestServerEndpointName.Http2WithTls, "2.0", true)]
#if NET5_0_OR_GREATER
        // Specifing HTTP/1.1 does work when the server is using TLS with HTTP/2
        // Caused by using HttpVersionPolicy.RequestVersionOrHigher setting
        [TestCase(TestServerEndpointName.Http2WithTls, "1.1", true)]
#else
        [TestCase(TestServerEndpointName.Http2WithTls, "1.1", false)]
#endif
        [TestCase(TestServerEndpointName.Http2WithTls, null, true)]
#if NET6_0_OR_GREATER
        [TestCase(TestServerEndpointName.Http3WithTls, null, true)]
#endif
        public async Task SendValidRequest_WithConnectionOptions(TestServerEndpointName endpointName, string? version, bool success)
        {
#if NET6_0_OR_GREATER
            if (endpointName == TestServerEndpointName.Http3WithTls &&
                !RequireHttp3Attribute.IsSupported(out var message))
            {
                Assert.Ignore(message);
            }
#endif

            SetExpectedErrorsFilter(writeContext =>
            {
                return !success;
            });

            // Arrage
            Version.TryParse(version, out var v);
            var channel = CreateGrpcWebChannel(endpointName, v);

            var client = new EchoService.EchoServiceClient(channel);

            // Act
            var call = client.EchoAsync(new EchoRequest { Message = "test" }).ResponseAsync.DefaultTimeout();

            // Assert
            if (success)
            {
                Assert.AreEqual("test", (await call.DefaultTimeout()).Message);
            }
            else
            {
                await ExceptionAssert.ThrowsAsync<RpcException>(async () => await call).DefaultTimeout();
            }
        }
    }
}
