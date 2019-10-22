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
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class ConnectionTests : FunctionalTestBase
    {
        [Test]
        public async Task ALPN_ProtocolDowngradedToHttp1_ThrowErrorFromServer()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.Message == "Call failed with gRPC error status. Status code: 'Internal', Message: 'Request protocol 'HTTP/1.1' is not supported.'.")
                {
                    return true;
                }

                return false;
            });

            var httpClient = Fixture.CreateClient(TestServerEndpointName.Http1WithTls);

            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpClient = httpClient
            });

            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(new HelloRequest { Name = "John" }).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
            Assert.AreEqual("Request protocol 'HTTP/1.1' is not supported.", ex.Status.Detail);
        }
    }
}
