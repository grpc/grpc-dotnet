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

using System.Net;
using Greet;
using Grpc.AspNetCore.Server.ClientFactory.Tests.TestObjects;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests;

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

    [Test]
    public async Task CreateClient_Unary_ServerCallContextHasValues_StateDisposed()
    {
        // Arrange
        var baseAddress = new Uri("http://localhost");
        var deadline = DateTime.UtcNow.AddDays(1);
        var cancellationToken = new CancellationTokenSource().Token;

        var interceptor = new OnDisposedInterceptor();

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline: deadline, cancellationToken: cancellationToken));
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = baseAddress;
            })
            .EnableCallContextPropagation()
            .AddInterceptor(() => interceptor)
            .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        var clientFactory = CreateGrpcClientFactory(serviceProvider);
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // Checking that token register calls don't build up on CTS and create a memory leak.
        var cts = new CancellationTokenSource();

        // Act
        // Send calls in a different method so there is no chance that a stack reference
        // to a gRPC call is still alive after calls are complete.
        var response = await client.SayHelloAsync(new HelloRequest(), cancellationToken: cts.Token);

        // Assert
        Assert.IsTrue(interceptor.ContextDisposed);
    }

    [Test]
    public async Task CreateClient_ServerStreaming_ServerCallContextHasValues_StateDisposed()
    {
        // Arrange
        var baseAddress = new Uri("http://localhost");
        var deadline = DateTime.UtcNow.AddDays(1);
        var cancellationToken = new CancellationTokenSource().Token;

        var interceptor = new OnDisposedInterceptor();

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline: deadline, cancellationToken: cancellationToken));
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = baseAddress;
            })
            .EnableCallContextPropagation()
            .AddInterceptor(() => interceptor)
            .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        var clientFactory = CreateGrpcClientFactory(serviceProvider);
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // Checking that token register calls don't build up on CTS and create a memory leak.
        var cts = new CancellationTokenSource();

        // Act
        // Send calls in a different method so there is no chance that a stack reference
        // to a gRPC call is still alive after calls are complete.
        var call = client.SayHellos(new HelloRequest(), cancellationToken: cts.Token);

        Assert.IsTrue(await call.ResponseStream.MoveNext());
        Assert.IsFalse(await call.ResponseStream.MoveNext());

        // Assert
        Assert.IsTrue(interceptor.ContextDisposed);
    }

    private sealed class OnDisposedInterceptor : Interceptor
    {
        public bool ContextDisposed { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(request, context);
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, context);
            return new AsyncUnaryCall<TResponse>(call.ResponseAsync,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () =>
                {
                    call.Dispose();
                    ContextDisposed = true;
                });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, context);
            return new AsyncServerStreamingCall<TResponse>(call.ResponseStream,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () =>
                {
                    call.Dispose();
                    ContextDisposed = true;
                });
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(context);
            return new AsyncClientStreamingCall<TRequest, TResponse>(call.RequestStream,
                call.ResponseAsync,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () =>
                {
                    call.Dispose();
                    ContextDisposed = true;
                });
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(context);
            return new AsyncDuplexStreamingCall<TRequest, TResponse>(call.RequestStream,
                call.ResponseStream,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () =>
                {
                    call.Dispose();
                    ContextDisposed = true;
                });
        }
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
    public async Task CreateClient_MultipleConfiguration_ConfigurationAppliedPerClient()
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
        services
            .AddGrpcClient<SecondGreeter.SecondGreeterClient>(o =>
            {
                o.Address = baseAddress;
            })
            .EnableCallContextPropagation()
            .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        var clientFactory = CreateGrpcClientFactory(serviceProvider);
        var client1 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
        var client2 = clientFactory.CreateClient<SecondGreeter.SecondGreeterClient>(nameof(SecondGreeter.SecondGreeterClient));

        // Act 1
        await client1.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync.DefaultTimeout();

        // Assert 1
        var log = testSink.Writes.Single(w => w.EventId.Name == "PropagateServerCallContextFailure");
        Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current HttpContext.", log.Message);

        // Act 2
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => client2.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync).DefaultTimeout();

        // Assert 2
        Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current HttpContext.", ex.Message);
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
