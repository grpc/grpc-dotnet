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

using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Shared.Server;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http.Timeouts;
#endif
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class CallHandlerTests
{
    private static readonly Marshaller<TestMessage> _marshaller = new Marshaller<TestMessage>((message, context) => { context.Complete(Array.Empty<byte>()); }, context => new TestMessage());

    [TestCase(MethodType.Unary, true)]
    [TestCase(MethodType.ClientStreaming, false)]
    [TestCase(MethodType.ServerStreaming, true)]
    [TestCase(MethodType.DuplexStreaming, false)]
    public async Task MinRequestBodyDataRateFeature_MethodType_HasRequestBodyDataRate(MethodType methodType, bool hasRequestBodyDataRate)
    {
        // Arrange
        var httpContext = HttpContextHelpers.CreateContext();
        var call = CreateHandler(methodType);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        Assert.AreEqual(hasRequestBodyDataRate, httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>()?.MinDataRate != null);
    }

    [TestCase(MethodType.Unary, true)]
    [TestCase(MethodType.ClientStreaming, false)]
    [TestCase(MethodType.ServerStreaming, true)]
    [TestCase(MethodType.DuplexStreaming, false)]
    public async Task MaxRequestBodySizeFeature_MethodType_HasMaxRequestBodySize(MethodType methodType, bool hasMaxRequestBodySize)
    {
        // Arrange
        var httpContext = HttpContextHelpers.CreateContext();
        var call = CreateHandler(methodType);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        Assert.AreEqual(hasMaxRequestBodySize, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize != null);
    }

    [Test]
    public async Task MaxRequestBodySizeFeature_FeatureIsReadOnly_FailureLogged()
    {
        // Arrange
        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, true);

        var httpContext = HttpContextHelpers.CreateContext(isMaxRequestBodySizeFeatureReadOnly: true);
        var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        Assert.AreEqual(true, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize != null);
        Assert.IsTrue(testSink.Writes.Any(w => w.EventId.Name == "UnableToDisableMaxRequestBodySizeLimit"));
    }

    [Test]
    public async Task ContentTypeValidation_InvalidContentType_FailureLogged()
    {
        // Arrange
        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, true);

        var httpContext = HttpContextHelpers.CreateContext(contentType: "text/plain");
        var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "UnsupportedRequestContentType");
        Assert.IsNotNull(log);
        Assert.AreEqual("Request content-type of 'text/plain' is not supported.", log!.Message);
    }

    [Test]
    public async Task SetResponseTrailers_FeatureMissing_ThrowError()
    {
        // Arrange
        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, true);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<TestService>();

        var httpContext = HttpContextHelpers.CreateContext(skipTrailerFeatureSet: true, serviceProvider: serviceCollection.BuildServiceProvider());
        var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.HandleCallAsync(httpContext)).DefaultTimeout();

        // Assert
        Assert.AreEqual("Trailers are not supported for this response. The server may not support gRPC.", ex.Message);
    }

    [Test]
    public async Task ProtocolValidation_InvalidProtocol_FailureLogged()
    {
        // Arrange
        var testSink = new TestSink();
        var testLoggerFactory = new TestLoggerFactory(testSink, true);

        var httpContext = HttpContextHelpers.CreateContext(protocol: "HTTP/1.1");
        var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "UnsupportedRequestProtocol");
        Assert.IsNotNull(log);
        Assert.AreEqual("Request protocol of 'HTTP/1.1' is not supported.", log!.Message);
    }

#if NET8_0_OR_GREATER
    [TestCase(MethodType.Unary, false)]
    [TestCase(MethodType.ClientStreaming, true)]
    [TestCase(MethodType.ServerStreaming, true)]
    [TestCase(MethodType.DuplexStreaming, true)]
    public async Task RequestTimeoutFeature_Global_DisableWhenStreaming(MethodType methodType, bool expectedTimeoutDisabled)
    {
        // Arrange
        var timeoutFeature = new TestHttpRequestTimeoutFeature();
        var httpContext = HttpContextHelpers.CreateContext();
        httpContext.Features.Set<IHttpRequestTimeoutFeature>(timeoutFeature);
        var call = CreateHandler(methodType);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        Assert.AreEqual(expectedTimeoutDisabled, timeoutFeature.TimeoutDisabled);
    }

    [TestCase(MethodType.Unary)]
    [TestCase(MethodType.ClientStreaming)]
    [TestCase(MethodType.ServerStreaming)]
    [TestCase(MethodType.DuplexStreaming)]
    public async Task RequestTimeoutFeature_WithEndpointMetadata_NotDisabledWhenStreaming(MethodType methodType)
    {
        // Arrange
        var timeoutFeature = new TestHttpRequestTimeoutFeature();
        var httpContext = HttpContextHelpers.CreateContext();
        httpContext.SetEndpoint(new Endpoint(c => Task.CompletedTask, new EndpointMetadataCollection(new RequestTimeoutAttribute(100)), "Test endpoint"));
        httpContext.Features.Set<IHttpRequestTimeoutFeature>(timeoutFeature);
        var call = CreateHandler(methodType);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        Assert.False(timeoutFeature.TimeoutDisabled);
    }

    private sealed class TestHttpRequestTimeoutFeature : IHttpRequestTimeoutFeature
    {
        public bool TimeoutDisabled { get; private set; }
        public CancellationToken RequestTimeoutToken { get; }

        public void DisableTimeout()
        {
            TimeoutDisabled = true;
        }
    }
#endif

    [Test]
    public async Task StatusDebugException_ErrorInHandler_SetInDebugException()
    {
        // Arrange
        var ex = new Exception("Test exception");
        var httpContext = HttpContextHelpers.CreateContext();
        var call = CreateHandler(MethodType.ClientStreaming, handlerAction: () => throw ex);

        // Act
        await call.HandleCallAsync(httpContext).DefaultTimeout();

        // Assert
        var serverCallContext = httpContext.Features.Get<IServerCallContextFeature>()!;
        Assert.AreEqual(ex, serverCallContext.ServerCallContext.Status.DebugException);
    }

    [Test]
    public async Task Deadline_HandleCallAsyncWaitsForDeadlineToFinish()
    {
        // Arrange
        Task? handleCallTask = null;
        bool? isHandleCallTaskCompleteDuringDeadline = null;
        var httpContext = HttpContextHelpers.CreateContext(completeAsyncAction: async () =>
        {
            await Task.Delay(200);
            isHandleCallTaskCompleteDuringDeadline = handleCallTask?.IsCompleted;
        });
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "50m";
        var call = CreateHandler(MethodType.ClientStreaming, handlerAction: () => Task.Delay(100));

        // Act
        handleCallTask = call.HandleCallAsync(httpContext).DefaultTimeout();
        await handleCallTask;

        // Assert
        var serverCallContext = httpContext.Features.Get<IServerCallContextFeature>()!;
        Assert.AreEqual(StatusCode.DeadlineExceeded, serverCallContext.ServerCallContext.Status.StatusCode);

        Assert.IsFalse(isHandleCallTaskCompleteDuringDeadline);
    }

    private static ServerCallHandlerBase<TestService, TestMessage, TestMessage> CreateHandler(
        MethodType methodType,
        ILoggerFactory? loggerFactory = null,
        Func<Task>? handlerAction = null)
    {
        var method = new Method<TestMessage, TestMessage>(methodType, "test", "test", _marshaller, _marshaller);

        switch (methodType)
        {
            case MethodType.Unary:
                return new UnaryServerCallHandler<TestService, TestMessage, TestMessage>(
                    new UnaryServerMethodInvoker<TestService, TestMessage, TestMessage>(
                        async (service, reader, context) =>
                        {
                            await (handlerAction?.Invoke() ?? Task.CompletedTask);
                            return new TestMessage();
                        },
                        method,
                        HttpContextServerCallContextHelper.CreateMethodOptions(),
                        new TestGrpcServiceActivator<TestService>()),
                    loggerFactory ?? NullLoggerFactory.Instance);
            case MethodType.ClientStreaming:
                return new ClientStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                    new ClientStreamingServerMethodInvoker<TestService, TestMessage, TestMessage>(
                        async (service, reader, context) =>
                        {
                            await (handlerAction?.Invoke() ?? Task.CompletedTask);
                            return new TestMessage();
                        },
                        method,
                        HttpContextServerCallContextHelper.CreateMethodOptions(),
                        new TestGrpcServiceActivator<TestService>()),
                    loggerFactory ?? NullLoggerFactory.Instance);
            case MethodType.ServerStreaming:
                return new ServerStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                    new ServerStreamingServerMethodInvoker<TestService, TestMessage, TestMessage>(
                        async (service, request, writer, context) =>
                        {
                            await (handlerAction?.Invoke() ?? Task.CompletedTask);
                        },
                        method,
                        HttpContextServerCallContextHelper.CreateMethodOptions(),
                        new TestGrpcServiceActivator<TestService>()),
                    loggerFactory ?? NullLoggerFactory.Instance);
            case MethodType.DuplexStreaming:
                return new DuplexStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                    new DuplexStreamingServerMethodInvoker<TestService, TestMessage, TestMessage>(
                        async (service, reader, writer, context) =>
                        {
                            await (handlerAction?.Invoke() ?? Task.CompletedTask);
                        },
                        method,
                        HttpContextServerCallContextHelper.CreateMethodOptions(),
                        new TestGrpcServiceActivator<TestService>()),
                    loggerFactory ?? NullLoggerFactory.Instance);
            default:
                throw new ArgumentException("Unexpected method type: " + methodType);
        }
    }
}

public class TestServiceProvider : IServiceProvider
{
    public static readonly TestServiceProvider Instance = new TestServiceProvider();

    public object GetService(Type serviceType)
    {
        throw new NotImplementedException();
    }
}

public class TestService { }

public class TestMessage { }
