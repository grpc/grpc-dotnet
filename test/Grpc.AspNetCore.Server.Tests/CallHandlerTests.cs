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
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
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
            Assert.AreEqual(hasRequestBodyDataRate, httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate != null);
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
            Assert.AreEqual(hasMaxRequestBodySize, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize != null);
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
            Assert.AreEqual(true, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize != null);
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
            Assert.AreEqual("Request content-type of 'text/plain' is not supported.", log.Message);
        }

        [Test]
        public async Task SetResponseTrailers_FeatureMissing_ThrowError()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var httpContext = HttpContextHelpers.CreateContext(skipTrailerFeatureSet: true);
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
            Assert.AreEqual("Request protocol of 'HTTP/1.1' is not supported.", log.Message);
        }

        [Test]
        public async Task ProtocolValidation_IISHttp2Protocol_Success()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var httpContext = HttpContextHelpers.CreateContext(protocol: GrpcProtocolConstants.Http20Protocol);
            var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

            // Act
            await call.HandleCallAsync(httpContext).DefaultTimeout();

            // Assert
            var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "UnsupportedRequestProtocol");
            Assert.IsNull(log);
        }

        private static ServerCallHandlerBase<TestService, TestMessage, TestMessage> CreateHandler(MethodType methodType, ILoggerFactory? loggerFactory = null)
        {
            var method = new Method<TestMessage, TestMessage>(methodType, "test", "test", _marshaller, _marshaller);

            switch (methodType)
            {
                case MethodType.Unary:
                    return new UnaryServerCallHandler<TestService, TestMessage, TestMessage>(
                        method,
                        (service, reader, context) => Task.FromResult(new TestMessage()),
                        new GrpcServiceOptions(),
                        loggerFactory ?? NullLoggerFactory.Instance,
                        new TestGrpcServiceActivator<TestService>(),
                        TestServiceProvider.Instance);
                case MethodType.ClientStreaming:
                    return new ClientStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                        method,
                        (service, reader, context) => Task.FromResult(new TestMessage()),
                        new GrpcServiceOptions(),
                        loggerFactory ?? NullLoggerFactory.Instance,
                        new TestGrpcServiceActivator<TestService>(),
                        TestServiceProvider.Instance);
                case MethodType.ServerStreaming:
                    return new ServerStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                        method,
                        (service, request, writer, context) => Task.FromResult(new TestMessage()),
                        new GrpcServiceOptions(),
                        loggerFactory ?? NullLoggerFactory.Instance,
                        new TestGrpcServiceActivator<TestService>(),
                        TestServiceProvider.Instance);
                case MethodType.DuplexStreaming:
                    return new DuplexStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                        method,
                        (service, reader, writer, context) => Task.CompletedTask,
                        new GrpcServiceOptions(),
                        loggerFactory ?? NullLoggerFactory.Instance,
                        new TestGrpcServiceActivator<TestService>(),
                        TestServiceProvider.Instance);
                default:
                    throw new ArgumentException();
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
}
