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
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcChannelTests
    {
        [Test]
        public void Build_SslCredentialsWithHttps_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials()
            });

            // Assert
            Assert.IsTrue(channel.IsSecure);
        }

        [Test]
        public void Build_NoHttpClient_InternalHttpClientHasInfiniteTimeout()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Assert
            Assert.AreEqual(Timeout.InfiniteTimeSpan, channel.HttpClient.Timeout);
        }

        [Test]
        public void Build_SslCredentialsWithHttp_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials()
            }));

            // Assert
            Assert.AreEqual("Channel is configured with secure channel credentials and can't use a HttpClient with a 'http' scheme.", ex.Message);
        }

        [Test]
        public void Build_SslCredentialsWithArgs_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = new SslCredentials("rootCertificates!!!")
            }));

            // Assert
            Assert.AreEqual(
                "SslCredentials with non-null arguments is not supported by GrpcChannel. " +
                "GrpcChannel uses HttpClient to make gRPC calls and HttpClient automatically loads root certificates from the operating system certificate store. " +
                "Client certificates should be configured on HttpClient. See https://aka.ms/AA6we64 for details.", ex.Message);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttp_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure
            });

            // Assert
            Assert.IsFalse(channel.IsSecure);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttps_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure
            }));

            // Assert
            Assert.AreEqual("Channel is configured with insecure channel credentials and can't use a HttpClient with a 'https' scheme.", ex.Message);
        }

        [Test]
        public void Dispose_NotCalled_NotDisposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act (nothing)

            // Assert
            Assert.IsFalse(channel.Disposed);
        }

        [Test]
        public void Dispose_Called_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.Throws<ObjectDisposedException>(() => channel.HttpClient.CancelPendingRequests());
        }

        [Test]
        public void Dispose_CalledMultipleTimes_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act
            channel.Dispose();
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
        }

        [Test]
        public void Dispose_CreateCallInvoker_ThrowError()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act
            channel.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
        }

        [Test]
        public async Task Dispose_StartCallOnClient_ThrowError()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");
            var client = new Greet.Greeter.GreeterClient(channel);

            // Act
            channel.Dispose();

            // Assert
            await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => client.SayHelloAsync(new Greet.HelloRequest()).ResponseAsync);
        }

        public class TestHttpMessageHandler : HttpMessageHandler
        {
            public bool Disposed { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
            }
        }

        [Test]
        public void Dispose_CalledWhenHttpClientSpecified_HttpClientNotDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsFalse(handler.Disposed);
        }

        [Test]
        public void Dispose_CalledWhenHttpClientSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = httpClient,
                DisposeHttpClient = true
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsTrue(handler.Disposed);
        }
    }
}
