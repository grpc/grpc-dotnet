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
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
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
        private static readonly Marshaller<TestMessage> _marshaller = new Marshaller<TestMessage>((message, context) => { }, context => new TestMessage());

        [TestCase(MethodType.Unary, true)]
        [TestCase(MethodType.ClientStreaming, false)]
        [TestCase(MethodType.ServerStreaming, true)]
        [TestCase(MethodType.DuplexStreaming, false)]
        public async Task MinRequestBodyDataRateFeature_MethodType_HasRequestBodyDataRate(MethodType methodType, bool hasRequestBodyDataRate)
        {
            // Arrange
            var httpContext = CreateContext();
            var call = CreateHandler(methodType);

            // Act
            await call.HandleCallAsync(httpContext);

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
            var httpContext = CreateContext();
            var call = CreateHandler(methodType);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.AreEqual(hasMaxRequestBodySize, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize != null);
        }

        [Test]
        public async Task MaxRequestBodySizeFeature_FeatureIsReadOnly_Logged()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var httpContext = CreateContext(isMaxRequestBodySizeFeatureReadOnly: true);
            var call = CreateHandler(MethodType.ClientStreaming, testLoggerFactory);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.AreEqual(true, httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize != null);
            Assert.IsTrue(testSink.Writes.Any(w => w.EventId.Name == "UnableToDisableMaxRequestBodySizeLimit"));
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

        private static HttpContext CreateContext(bool isMaxRequestBodySizeFeatureReadOnly = false)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Protocol = GrpcProtocolConstants.Http2Protocol;
            httpContext.Request.ContentType = GrpcProtocolConstants.GrpcContentType;
            httpContext.Features.Set<IHttpMinRequestBodyDataRateFeature>(new TestMinRequestBodyDataRateFeature());
            httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestMaxRequestBodySizeFeature(isMaxRequestBodySizeFeatureReadOnly, 100));

            return httpContext;
        }
    }

    public class TestService { }

    public class TestMessage { }

    public class TestMinRequestBodyDataRateFeature : IHttpMinRequestBodyDataRateFeature
    {
        public MinDataRate MinDataRate { get; set; } = new MinDataRate(1, TimeSpan.FromSeconds(5));
    }

    public class TestMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public TestMaxRequestBodySizeFeature(bool isReadOnly, long? maxRequestBodySize)
        {
            IsReadOnly = isReadOnly;
            MaxRequestBodySize = maxRequestBodySize;
        }

        public bool IsReadOnly { get; }
        public long? MaxRequestBodySize { get; set; }
    }

    internal class TestGrpcServiceActivator<TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class
    {
        public GrpcActivatorHandle<TGrpcService> Create(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }

        public ValueTask ReleaseAsync(GrpcActivatorHandle<TGrpcService> service)
        {
            throw new NotImplementedException();
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
}
