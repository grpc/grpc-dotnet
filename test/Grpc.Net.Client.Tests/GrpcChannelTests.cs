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
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Testing;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcChannelTests
    {
        [Test]
        public void Build_AddressWithoutHost_Error()
        {
            // Arrange & Act
            var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("test.example.com:5001"))!;

            // Assert
            Assert.AreEqual("Address 'test.example.com:5001' doesn't have a host. Address should include a scheme, host, and optional port. For example, 'https://localhost:5001'.", ex.Message);
        }

        [TestCase("https://localhost:5001/path", true)]
        [TestCase("https://localhost:5001/?query=ya", true)]
        [TestCase("https://localhost:5001//", true)]
        [TestCase("https://localhost:5001/", false)]
        [TestCase("https://localhost:5001", false)]
        public void Build_AddressWithPath_Log(string address, bool hasPathOrQuery)
        {
            // Arrange
            var testSink = new TestSink();
            var testFactory = new TestLoggerFactory(testSink, enabled: true);

            // Act
            GrpcChannel.ForAddress(address, CreateGrpcChannelOptions(o => o.LoggerFactory = testFactory));

            // Assert
            var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "AddressPathUnused");
            if (hasPathOrQuery)
            {
                Assert.IsNotNull(log);
                Assert.AreEqual(LogLevel.Debug, log!.LogLevel);

                var message = $"The path in the channel's address '{address}' won't be used when making gRPC calls. " +
                    "A DelegatingHandler can be used to include a path with gRPC calls. See https://aka.ms/aspnet/grpc/subdir for details.";
                Assert.AreEqual(message, log.Message);
            }
            else
            {
                Assert.IsNull(log);
            }
        }

        [Test]
        public void Build_SslCredentialsWithHttps_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("https://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials()));

            // Assert
            Assert.IsTrue(channel.IsSecure);
        }

        [Test]
        public void Build_SslCredentialsWithHttp_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("http://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials())))!;

            // Assert
            Assert.AreEqual("Channel is configured with secure channel credentials and can't use a HttpClient with a 'http' scheme.", ex.Message);
        }

        [Test]
        public void Build_SslCredentialsWithArgs_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials("rootCertificates!!!"))))!;

            // Assert
            Assert.AreEqual(
                "SslCredentials with non-null arguments is not supported by GrpcChannel. " +
                "GrpcChannel uses HttpClient to make gRPC calls and HttpClient automatically loads root certificates from the operating system certificate store. " +
                "Client certificates should be configured on HttpClient. See https://aka.ms/aspnet/grpc/certauth for details.", ex.Message);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttp_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("http://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure));

            // Assert
            Assert.IsFalse(channel.IsSecure);
        }

        private static GrpcChannelOptions CreateGrpcChannelOptions(Action<GrpcChannelOptions>? func = null)
        {
            var o = new GrpcChannelOptions();
#if NET472
            // An error is thrown if no handler is specified by .NET Standard 2.0 target.
            o.HttpHandler = new NullHttpHandler();
#endif
            func?.Invoke(o);
            return o;
        }

        [Test]
        public void Build_InsecureCredentialsWithHttps_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure)))!;

            // Assert
            Assert.AreEqual("Channel is configured with insecure channel credentials and can't use a HttpClient with a 'https' scheme.", ex.Message);
        }

        [Test]
        public void Build_HttpClientAndHttpHandler_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = new HttpClient(),
                HttpHandler = new HttpClientHandler()
            }))!;

            // Assert
            Assert.AreEqual("HttpClient and HttpHandler have been configured. Only one HTTP caller can be specified.", ex.Message);
        }

        [Test]
        public async Task Build_HttpClient_UsedForRequestsAsync()
        {
            // Arrange
            var handler = new ExceptionHttpMessageHandler("HttpClient");
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = new HttpClient(handler)
            });
            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest()));

            // Assert
            Assert.AreEqual("HttpClient", ex.Status.DebugException.Message);
        }

        [Test]
        public async Task Build_HttpHandler_UsedForRequestsAsync()
        {
            // Arrange
            var handler = new ExceptionHttpMessageHandler("HttpHandler");
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler
            });
            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest()));

            // Assert
            Assert.AreEqual("HttpHandler", ex.Status.DebugException.Message);
        }

#if NET472
        [Test]
        public void Build_NoHttpProviderOnNetFx_Throw()
        {
            // Arrange & Act
            var ex = Assert.Throws<PlatformNotSupportedException>(() => GrpcChannel.ForAddress("https://localhost"))!;

            // Assert
            var message =
                $"gRPC requires extra configuration on .NET implementations that don't support gRPC over HTTP/2. " +
                $"An HTTP provider must be specified using {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpHandler)}." +
                $"The configured HTTP provider must either support HTTP/2 or be configured to use gRPC-Web. " +
                $"See https://aka.ms/aspnet/grpc/netstandard for details.";

            Assert.AreEqual(message, ex.Message);
        }
#endif

        [Test]
        public void Build_ServiceConfigDuplicateMethodConfigNames_Error()
        {
            // Arrange
            var options = CreateGrpcChannelOptions(o => o.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig { Names = { MethodName.Default } },
                    new MethodConfig { Names = { MethodName.Default } }
                }
            });

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", options))!;

            // Assert
            Assert.AreEqual("Duplicate method config found. Service: '', method: ''.", ex.Message);
        }

        [Test]
        public void Dispose_NotCalled_NotDisposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

            // Act (nothing)

            // Assert
            Assert.IsFalse(channel.Disposed);
        }

#if !NET472
        [Test]
        public void Dispose_Called_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.Throws<ObjectDisposedException>(() => channel.HttpInvoker.SendAsync(new HttpRequestMessage(), CancellationToken.None));
        }
#endif

        [Test]
        public void Dispose_CalledMultipleTimes_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

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
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

            // Act
            channel.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
        }

        [Test]
        public async Task Dispose_StartCallOnClient_ThrowError()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());
            var client = new Greet.Greeter.GreeterClient(channel);

            // Act
            channel.Dispose();

            // Assert
            await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => client.SayHelloAsync(new Greet.HelloRequest()).ResponseAsync);
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

        [Test]
        public void Dispose_CalledWhenHttpMessageHandlerSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsTrue(handler.Disposed);
        }

        [Test]
        public async Task Dispose_CalledWhileActiveCalls_ActiveCallsDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new Greeter.GreeterClient(channel);
            var call = client.SayHelloAsync(new HelloRequest());

            var exTask = ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
            Assert.IsFalse(exTask.IsCompleted);
            Assert.AreEqual(1, channel.ActiveCalls.Count);

            // Act
            channel.Dispose();

            // Assert
            var ex = await exTask.DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);

            Assert.IsTrue(channel.Disposed);
            Assert.AreEqual(0, channel.ActiveCalls.Count);
        }

        public class TestHttpMessageHandler : HttpMessageHandler
        {
            public bool Disposed { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var tcs = new TaskCompletionSource<HttpResponseMessage>();
                cancellationToken.Register(s => ((TaskCompletionSource<HttpResponseMessage>)s!).SetException(new OperationCanceledException()), tcs);
                return await tcs.Task;
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
            }
        }

        public class ExceptionHttpMessageHandler : HttpMessageHandler
        {
            public string ExceptionMessage { get; }

            public ExceptionHttpMessageHandler(string exceptionMessage)
            {
                ExceptionMessage = exceptionMessage;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException(ExceptionMessage));
            }
        }
    }
}
