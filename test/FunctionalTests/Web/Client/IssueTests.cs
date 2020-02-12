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

using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Net.Client;
using Issue;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client
{
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
    public class IssueTests : GrpcWebFunctionalTestBase
    {
        public IssueTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
         : base(grpcTestMode, endpointName)
        {
        }

        // https://github.com/grpc/grpc-dotnet/issues/752
        [Test]
        public async Task SendLargeRequest_SuccessResponse()
        {
            // Arrage
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new IssueService.IssueServiceClient(channel);
            var request = new GetLibraryRequest();
            request.UserId = "admin";
            request.SearchTerm = string.Empty;
            for (var i = 0; i < 4096; i++)
            {
                request.Carriers.Add(i.ToString());
            }

            // Act
            var response = await client.GetLibraryAsync(request);

            // Assert
            Assert.AreEqual("admin", response.UserId);
        }
    }
}
