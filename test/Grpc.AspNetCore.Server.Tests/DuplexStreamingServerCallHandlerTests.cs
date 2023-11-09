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

using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Shared.Server;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class DuplexStreamingServerCallHandlerTests
{
    private static readonly Marshaller<TestMessage> _marshaller = new Marshaller<TestMessage>((message, context) => { context.Complete(Array.Empty<byte>()); }, context => new TestMessage());

    [Test]
    public async Task HandleCallAsync_ConcurrentReadAndWrite_Success()
    {
        // Arrange
        var invoker = new DuplexStreamingServerMethodInvoker<TestService, TestMessage, TestMessage>(
            (service, reader, writer, context) =>
            {
                var message = new TestMessage();
                var readTask = Task.Run(() => reader.MoveNext());
                var writeTask = Task.Run(() => writer.WriteAsync(message));
                return Task.WhenAll(readTask, writeTask);
            },
            new Method<TestMessage, TestMessage>(MethodType.DuplexStreaming, "test", "test", _marshaller, _marshaller),
            HttpContextServerCallContextHelper.CreateMethodOptions(),
            new TestGrpcServiceActivator<TestService>());
        var handler = new DuplexStreamingServerCallHandler<TestService, TestMessage, TestMessage>(invoker, NullLoggerFactory.Instance);

        // Verify there isn't a race condition when reading/writing on seperate threads.
        // This test primarily exists to ensure that the stream reader and stream writer aren't accessing non-thread safe APIs on HttpContext.
        for (var i = 0; i < 10_000; i++)
        {
            var httpContext = HttpContextHelpers.CreateContext();

            // Act
            await handler.HandleCallAsync(httpContext);

            // Assert
            var trailers = httpContext.Features.Get<IHttpResponseTrailersFeature>()!.Trailers;
            Assert.AreEqual("0", trailers["grpc-status"].ToString());
        }
    }
}
