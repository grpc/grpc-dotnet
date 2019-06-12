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
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcClientFactoryTests
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost")
        };

        [Test]
        public void Create_WithClientBaseClient_ReturnInstance()
        {
            // Arrange & Act
            var client = GrpcClient.Create<Greet.Greeter.GreeterClient>(_httpClient);

            // Assert
            Assert.IsNotNull(client);
        }

        [Test]
        public void Create_WithLiteClientBaseClient_ReturnInstance()
        {
            // Arrange & Act
            var client = GrpcClient.Create<CoreGreet.Greeter.GreeterClient>(_httpClient);

            // Assert
            Assert.IsNotNull(client);
        }

        [Test]
        public void Create_WithNonCompatibleClient_throws()
        {
            // Arrange, Act, Assert
            Assert.Throws<InvalidOperationException>(() => GrpcClient.Create<GrpcClientFactoryTests>(_httpClient));
        }
    }
}
