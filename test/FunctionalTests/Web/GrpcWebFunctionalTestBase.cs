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
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace Grpc.AspNetCore.FunctionalTests.Web
{
    public enum GrpcTestMode
    {
        Grpc,
        GrpcWeb,
        GrpcWebText
    }

    public abstract class GrpcWebFunctionalTestBase : FunctionalTestBase
    {
        private readonly GrpcTestMode _grpcTestMode;
        private readonly TestServerEndpointName _endpointName;

        protected GrpcWebFunctionalTestBase(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
        {
            _grpcTestMode = grpcTestMode;
            _endpointName = endpointName;
        }

        protected HttpClient CreateGrpcWebClient()
        {
            var protocol = _endpointName == TestServerEndpointName.Http1
                ? new Version(1, 1)
                : new Version(2, 0);

            GrpcWebHandler? grpcWebHandler = null;
            if (_grpcTestMode != GrpcTestMode.Grpc)
            {
                var mode = _grpcTestMode == GrpcTestMode.GrpcWeb ? GrpcWebMode.GrpcWeb : GrpcWebMode.GrpcWebText;
                grpcWebHandler = new GrpcWebHandler(mode)
                {
                    HttpVersion = protocol
                };
            }

            return Fixture.CreateClient(_endpointName, grpcWebHandler);
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
}
