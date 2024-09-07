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

using System.Net.Http;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Gateway.Testing;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client;

public class ConnectionTests : FunctionalTestBase
{
    private HttpClient CreateGrpcWebClient(TestServerEndpointName endpointName, Version? version)
    {
        GrpcWebHandler grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb);
#pragma warning disable CS0618 // Type or member is obsolete
        grpcWebHandler.HttpVersion = version;
#pragma warning restore CS0618 // Type or member is obsolete

        return Fixture.CreateClient(endpointName, grpcWebHandler);
    }

    private GrpcChannel CreateGrpcWebChannel(TestServerEndpointName endpointName, Version? version, bool setVersionOnHandler)
    {
        var options = new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory
        };
        if (setVersionOnHandler)
        {
            options.HttpClient = CreateGrpcWebClient(endpointName, version: null);
            if (version != null)
            {
                options.HttpVersion = version;
                options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            }
            return GrpcChannel.ForAddress(options.HttpClient.BaseAddress!, options);
        }
        else
        {
            options.HttpClient = CreateGrpcWebClient(endpointName, version);
            return GrpcChannel.ForAddress(options.HttpClient.BaseAddress!, options);
        }
    }

    private static IEnumerable<TestCaseData> ConnectionTestData()
    {
        yield return new TestCaseData(TestServerEndpointName.Http1, "2.0", false);
        yield return new TestCaseData(TestServerEndpointName.Http1, "1.1", true);
        yield return new TestCaseData(TestServerEndpointName.Http1, null, false);
        yield return new TestCaseData(TestServerEndpointName.Http2, "2.0", true);
        yield return new TestCaseData(TestServerEndpointName.Http2, "1.1", false);
        yield return new TestCaseData(TestServerEndpointName.Http2, null, true);
        // Specifing HTTP/2 doesn't work when the server is using TLS with HTTP/1.1
        // Caused by using HttpVersionPolicy.RequestVersionOrHigher setting
        yield return new TestCaseData(TestServerEndpointName.Http1WithTls, "2.0", false);
        yield return new TestCaseData(TestServerEndpointName.Http1WithTls, "1.1", true);
        yield return new TestCaseData(TestServerEndpointName.Http1WithTls, null, true);
        yield return new TestCaseData(TestServerEndpointName.Http2WithTls, "2.0", true);
        // Specifing HTTP/1.1 does work when the server is using TLS with HTTP/2
        // Caused by using HttpVersionPolicy.RequestVersionOrHigher setting
        yield return new TestCaseData(TestServerEndpointName.Http2WithTls, "1.1", true);
        yield return new TestCaseData(TestServerEndpointName.Http2WithTls, null, true);
#if NET7_0_OR_GREATER
        yield return new TestCaseData(TestServerEndpointName.Http3WithTls, null, true);
#endif
    }

    [TestCaseSource(nameof(ConnectionTestData))]
    public async Task SendValidRequest_WithConnectionOptionsOnHandler(TestServerEndpointName endpointName, string? version, bool success)
    {
        await SendRequestWithConnectionOptionsCore(endpointName, version, success, setVersionOnHandler: true);
    }

    [TestCaseSource(nameof(ConnectionTestData))]
    public async Task SendValidRequest_WithConnectionOptionsOnChannel(TestServerEndpointName endpointName, string? version, bool success)
    {
        await SendRequestWithConnectionOptionsCore(endpointName, version, success, setVersionOnHandler: false);
    }

    private async Task SendRequestWithConnectionOptionsCore(TestServerEndpointName endpointName, string? version, bool success, bool setVersionOnHandler)
    {
        if (endpointName == TestServerEndpointName.Http3WithTls &&
            !RequireHttp3Attribute.IsSupported(out var message))
        {
            Assert.Ignore(message);
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return !success;
        });

        // Arrage
        Version.TryParse(version, out var v);
        var channel = CreateGrpcWebChannel(endpointName, v, setVersionOnHandler);

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
