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

using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web;

public enum GrpcTestMode
{
    Grpc,
    GrpcWeb,
    GrpcWebText
}

public abstract class GrpcWebFunctionalTestBase : FunctionalTestBase
{
    public GrpcTestMode GrpcTestMode { get; }
    public TestServerEndpointName EndpointName { get; }

    protected GrpcWebFunctionalTestBase(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
    {
        GrpcTestMode = grpcTestMode;
        EndpointName = endpointName;

        if (endpointName == TestServerEndpointName.Http3WithTls &&
            !RequireHttp3Attribute.IsSupported(out var message))
        {
            Assert.Ignore(message);
        }
    }

    protected HttpClient CreateGrpcWebClient()
    {
        var grpcWebHandler = CreateGrpcWebHandlerCore();

        return Fixture.CreateClient(EndpointName, grpcWebHandler);
    }

    protected (HttpMessageHandler handler, Uri address) CreateGrpcWebHandler()
    {
        var grpcWebHandler = CreateGrpcWebHandlerCore();

        return Fixture.CreateHandler(EndpointName, grpcWebHandler);
    }

    private GrpcWebHandler? CreateGrpcWebHandlerCore()
    {
        Version protocol;

        if (EndpointName == TestServerEndpointName.Http1)
        {
            protocol = new Version(1, 1);
        }
        else if (EndpointName == TestServerEndpointName.Http3WithTls)
        {
            protocol = new Version(3, 0);
        }
        else
        {
            protocol = new Version(2, 0);
        }

        GrpcWebHandler? grpcWebHandler = null;
        if (GrpcTestMode != GrpcTestMode.Grpc)
        {
            var mode = GrpcTestMode == GrpcTestMode.GrpcWeb ? GrpcWebMode.GrpcWeb : GrpcWebMode.GrpcWebText;
            grpcWebHandler = new GrpcWebHandler(mode)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                HttpVersion = protocol
#pragma warning restore CS0618 // Type or member is obsolete
            };
        }

        return grpcWebHandler;
    }

    protected GrpcChannel CreateGrpcWebChannel()
    {
        var httpClient = CreateGrpcWebClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient,
            LoggerFactory = LoggerFactory
        });

        return channel;
    }
}
