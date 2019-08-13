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
using Grpc.Core;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcChannelTests
    {
        [Test]
        public void Build_SslCredentialsWithHttps_Success()
        {
            // Arrange & Assert
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials()
            });

            // Act
            Assert.IsTrue(channel.IsSecure);
        }

        [Test]
        public void Build_SslCredentialsWithHttp_ThrowsError()
        {
            // Arrange & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials()
            }));

            // Act
            Assert.AreEqual("Channel is configured with secure channel credentials and can't use a HttpClient with a 'http' scheme.", ex.Message);
        }

        [Test]
        public void Build_SslCredentialsWithArgs_ThrowsError()
        {
            // Arrange & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials("rootCertificates!!!")
            }));

            // Act
            Assert.AreEqual("Using SslCredentials with non-null arguments is not supported by GrpcChannel.", ex.Message);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttp_Success()
        {
            // Arrange & Assert
            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure
            });

            // Act
            Assert.IsFalse(channel.IsSecure);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttps_ThrowsError()
        {
            // Arrange & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure
            }));

            // Act
            Assert.AreEqual("Channel is configured with insecure channel credentials and can't use a HttpClient with a 'https' scheme.", ex.Message);
        }
    }
}
