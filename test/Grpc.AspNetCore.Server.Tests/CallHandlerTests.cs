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
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class CallHandlerTests
    {
        private static readonly Marshaller<TestMessage> _marshaller = new Marshaller<TestMessage>((message, context) => { }, context => new TestMessage());

        [Test]
        public async Task MinRequestBodyDataRateFeature_ClientStreaming_DisablesRequestBodyDataRate()
        {
            // Arrange
            var httpContext = CreateContext();
            var method = new Method<TestMessage, TestMessage>(MethodType.ClientStreaming, "test", "test", _marshaller, _marshaller);
            var call = new ClientStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                method,
                (service, reader, context) => Task.FromResult(new TestMessage()),
                new GrpcServiceOptions(),
                NullLoggerFactory.Instance);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.Null(httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate);
        }

        [Test]
        public async Task MinRequestBodyDataRateFeature_DuplexStreaming_DisablesRequestBodyDataRate()
        {
            // Arrange
            var httpContext = CreateContext();
            var method = new Method<TestMessage, TestMessage>(MethodType.DuplexStreaming, "test", "test", _marshaller, _marshaller);
            var call = new DuplexStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                method,
                (service, reader, writer, context) => Task.CompletedTask,
                new GrpcServiceOptions(),
                NullLoggerFactory.Instance);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.Null(httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate);
        }

        [Test]
        public async Task MinRequestBodyDataRateFeature_Unary_DoesNotDisableRequestBodyDataRate()
        {
            // Arrange
            var httpContext = CreateContext();
            var method = new Method<TestMessage, TestMessage>(MethodType.Unary, "test", "test", _marshaller, _marshaller);
            var call = new UnaryServerCallHandler<TestService, TestMessage, TestMessage>(
                method,
                (service, request, context) => Task.FromResult(new TestMessage()),
                new GrpcServiceOptions(),
                NullLoggerFactory.Instance);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.NotNull(httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate);
        }

        [Test]
        public async Task MinRequestBodyDataRateFeature_ServerStreaming_DoesNotDisableRequestBodyDataRate()
        {
            // Arrange
            var httpContext = CreateContext();
            var method = new Method<TestMessage, TestMessage>(MethodType.ServerStreaming, "test", "test", _marshaller, _marshaller);
            var call = new ServerStreamingServerCallHandler<TestService, TestMessage, TestMessage>(
                method,
                (service, request, writer, context) => Task.FromResult(new TestMessage()),
                new GrpcServiceOptions(),
                NullLoggerFactory.Instance);

            // Act
            await call.HandleCallAsync(httpContext);

            // Assert
            Assert.NotNull(httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate);
        }

        private static HttpContext CreateContext()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/grpc";
            httpContext.Features.Set<IHttpMinRequestBodyDataRateFeature>(new TestMinRequestBodyDataRateFeature());

            return httpContext;
        }
    }

    public class TestService { }

    public class TestMessage { }

    public class TestMinRequestBodyDataRateFeature : IHttpMinRequestBodyDataRateFeature
    {
        public MinDataRate MinDataRate { get; set; } = new MinDataRate(1, TimeSpan.FromSeconds(5));
    }
}
