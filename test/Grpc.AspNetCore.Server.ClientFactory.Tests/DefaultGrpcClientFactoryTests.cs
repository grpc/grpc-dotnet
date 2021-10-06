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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.ClientFactory.Tests.TestObjects;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class DefaultGrpcClientFactoryTests
    {
        [Test]
        public void EnableCallContextPropagation_NotFromGrpcClientFactory_ThrowError()
        {
            var services = new ServiceCollection();
            var clientBuilder = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => clientBuilder.EnableCallContextPropagation())!;
            Assert.AreEqual("EnableCallContextPropagation must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public void EnableCallContextPropagation_NotFromGrpcClientFactoryAndExistingGrpcClient_ThrowError()
        {
            var services = new ServiceCollection();
            services.AddGrpcClient<Greeter.GreeterClient>();
            var clientBuilder = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => clientBuilder.EnableCallContextPropagation())!;
            Assert.AreEqual("EnableCallContextPropagation must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public async Task CreateClient_ServerCallContextHasValues_PropogatedDeadlineAndCancellation()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");
            var deadline = DateTime.UtcNow.AddDays(1);
            var cancellationToken = new CancellationTokenSource().Token;

            CallOptions options = default;

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline, cancellationToken));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation()
                .AddInterceptor(() => new CallbackInterceptor(o => options = o))
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(deadline, options.Deadline);
            Assert.AreEqual(cancellationToken, options.CancellationToken);
        }

        [TestCase(Canceller.Context)]
        [TestCase(Canceller.User)]
        public async Task CreateClient_ServerCallContextAndUserCancellationToken_PropogatedDeadlineAndCancellation(Canceller canceller)
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");
            var deadline = DateTime.UtcNow.AddDays(1);
            var contextCts = new CancellationTokenSource();
            var userCts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            CallOptions options = default;

            var handler = TestHttpMessageHandler.Create(async (r, token) =>
            {
                token.Register(() => tcs.SetCanceled());

                await tcs.Task;

                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline, contextCts.Token));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation()
                .AddInterceptor(() => new CallbackInterceptor(o => options = o))
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            using var call = client.SayHelloAsync(new HelloRequest(), cancellationToken: userCts.Token);
            var responseTask = call.ResponseAsync;

            // Assert
            Assert.AreEqual(deadline, options.Deadline);

            // CancellationToken passed to call is a linked cancellation token.
            // It's created from the context and user tokens.
            Assert.AreNotEqual(contextCts.Token, options.CancellationToken);
            Assert.AreNotEqual(userCts.Token, options.CancellationToken);
            Assert.AreNotEqual(CancellationToken.None, options.CancellationToken);

            Assert.IsFalse(responseTask.IsCompleted);

            // Either CTS should cancel call.
            switch (canceller)
            {
                case Canceller.Context:
                    contextCts.Cancel();
                    break;
                case Canceller.User:
                    userCts.Cancel();
                    break;
            }

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
            Assert.Throws<InvalidOperationException>(() => call.GetTrailers());
        }

        public enum Canceller
        {
            None,
            Context,
            User
        }

        [Test]
        public async Task CreateClient_NoHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(null));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation()
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current HttpContext.", ex.Message);
        }

        [Test]
        public async Task CreateClient_NoHttpContextIgnoreError_Success()
        {
            // Arrange
            var testSink = new TestSink();
            var testProvider = new TestLoggerProvider(testSink);

            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(null));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation(o => o.SuppressContextNotFoundErrors = true)
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            await client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync.DefaultTimeout();

            // Assert
            var log = testSink.Writes.Single(w => w.EventId.Name == "PropagateServerCallContextFailure");
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current HttpContext.", log.Message);
        }

        [Test]
        public async Task CreateClient_NoServerCallContextOnHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(new DefaultHttpContext()));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation()
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the gRPC ServerCallContext on the current HttpContext.", ex.Message);
        }

        [Test]
        public async Task CreateClient_NoServerCallContextOnHttpContextIgnoreError_Success()
        {
            // Arrange
            var testSink = new TestSink();
            var testProvider = new TestLoggerProvider(testSink);

            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(new DefaultHttpContext()));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = baseAddress;
                })
                .EnableCallContextPropagation(o => o.SuppressContextNotFoundErrors = true)
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            await client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync.DefaultTimeout();

            // Assert
            var log = testSink.Writes.Single(w => w.EventId.Name == "PropagateServerCallContextFailure");
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the gRPC ServerCallContext on the current HttpContext.", log.Message);
        }

        private IHttpContextAccessor CreateHttpContextAccessor(HttpContext? httpContext)
        {
            return new TestHttpContextAccessor(httpContext);
        }

        private IHttpContextAccessor CreateHttpContextAccessorWithServerCallContext(DateTime deadline = default, CancellationToken cancellationToken = default)
        {
            var httpContext = new DefaultHttpContext();
            var serverCallContext = new TestServerCallContext(deadline, cancellationToken);
            var serverCallContextFeature = new TestServerCallContextFeature(serverCallContext);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContextFeature);

            return CreateHttpContextAccessor(httpContext);
        }

        private class TestHttpContextAccessor : IHttpContextAccessor
        {
            public TestHttpContextAccessor(HttpContext? httpContext)
            {
                HttpContext = httpContext;
            }

            public HttpContext? HttpContext { get; set; }
        }

        private static DefaultGrpcClientFactory CreateGrpcClientFactory(ServiceProvider serviceProvider)
        {
            return new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<GrpcCallInvokerFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>());
        }
    }
}
