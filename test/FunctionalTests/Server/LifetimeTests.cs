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

using System.IO;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Tests.Shared;
using Lifetime;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class LifetimeTests : FunctionalTestBase
    {
        [Test]
        public async Task Scoped_ReturnSameValue()
        {
            // Arrange & Act
            var valueResponse1 = await CallLifetimeService("Lifetime.LifetimeService/GetScopedValue").DefaultTimeout();
            var valueResponse2 = await CallLifetimeService("Lifetime.LifetimeService/GetScopedValue").DefaultTimeout();
            var valueResponse3 = await CallLifetimeService("Lifetime.LifetimeService/GetScopedValue").DefaultTimeout();

            // Assert
            Assert.AreEqual(1, valueResponse1.Value);
            Assert.AreEqual(1, valueResponse2.Value);
            Assert.AreEqual(1, valueResponse3.Value);
        }

        [Test]
        public async Task Transient_ReturnSameValue()
        {
            // Arrange & Act
            var valueResponse1 = await CallLifetimeService("Lifetime.LifetimeService/GetTransientValue").DefaultTimeout();
            var valueResponse2 = await CallLifetimeService("Lifetime.LifetimeService/GetTransientValue").DefaultTimeout();
            var valueResponse3 = await CallLifetimeService("Lifetime.LifetimeService/GetTransientValue").DefaultTimeout();

            // Assert
            Assert.AreEqual(1, valueResponse1.Value);
            Assert.AreEqual(1, valueResponse2.Value);
            Assert.AreEqual(1, valueResponse3.Value);
        }

        [Test]
        public async Task Singleton_ReturnIncrementingValue()
        {
            // Arrange & Act
            var valueResponse1 = await CallLifetimeService("Lifetime.LifetimeService/GetSingletonValue").DefaultTimeout();
            var valueResponse2 = await CallLifetimeService("Lifetime.LifetimeService/GetSingletonValue").DefaultTimeout();
            var valueResponse3 = await CallLifetimeService("Lifetime.LifetimeService/GetSingletonValue").DefaultTimeout();

            // Assert
            Assert.AreEqual(1, valueResponse1.Value);
            Assert.AreEqual(2, valueResponse2.Value);
            Assert.AreEqual(3, valueResponse3.Value);
        }

        private async Task<ValueResponse> CallLifetimeService(string path)
        {
            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, new Empty());

            var response = await Fixture.Client.PostAsync(path, new GrpcStreamContent(requestStream)).DefaultTimeout();

            return MessageHelpers.AssertReadMessage<ValueResponse>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
        }
    }
}
