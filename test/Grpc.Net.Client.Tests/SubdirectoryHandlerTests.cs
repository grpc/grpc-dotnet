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
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class SubdirectoryHandlerTests
{
    [Test]
    public async Task AsyncUnaryCall_SubdirectoryHandlerConfigured_RequestUriChanged()
    {
        // Arrange
        HttpRequestMessage? httpRequestMessage = null;

        var handler = TestHttpMessageHandler.Create(async r =>
        {
            httpRequestMessage = r;

            var reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var httpClient = new HttpClient(new SubdirectoryHandler(handler, "/TestSubdirectory"));
        httpClient.BaseAddress = new Uri("https://localhost:5001");

        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

        // Assert
        Assert.AreEqual("Hello world", rs.Message);

        Assert.IsNotNull(httpRequestMessage);
        Assert.AreEqual(new Version(2, 0), httpRequestMessage!.Version);
        Assert.AreEqual(HttpMethod.Post, httpRequestMessage.Method);
        Assert.AreEqual(new Uri("https://localhost:5001/TestSubdirectory/ServiceName/MethodName"), httpRequestMessage.RequestUri);
    }

    /// <summary>
    /// A delegating handler that will add a subdirectory to the URI of gRPC requests.
    /// </summary>
    public class SubdirectoryHandler : DelegatingHandler
    {
        private readonly string _subdirectory;

        public SubdirectoryHandler(HttpMessageHandler innerHandler, string subdirectory) : base(innerHandler)
        {
            _subdirectory = subdirectory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = $"{request.RequestUri!.Scheme}://{request.RequestUri.Host}:{request.RequestUri.Port}{_subdirectory}{request.RequestUri.AbsolutePath}";
            request.RequestUri = new Uri(url, UriKind.Absolute);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
