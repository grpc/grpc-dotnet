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
using Compression;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class CompressionTests : FunctionalTestBase
    {
        [Test]
        public async Task SendCompressedMessage_ServiceHasNoCompressionConfigured_ResponseIdentityEncoding()
        {
            // Arrange
            var compressionMetadata = CreateClientCompressionMetadata("gzip");

            var client = GrpcClient.Create<CompressionService.CompressionServiceClient>(Fixture.Client, LoggerFactory);

            // Act
            var call = client.SayHelloAsync(new HelloRequest { Name = "World" }, headers: compressionMetadata);
            var response = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello World", response.Message);
        }

        private static Metadata CreateClientCompressionMetadata(string algorithmName)
        {
            return new Metadata
            {
                { new Metadata.Entry("grpc-internal-encoding-request", algorithmName) }
            };
        }
    }
}
