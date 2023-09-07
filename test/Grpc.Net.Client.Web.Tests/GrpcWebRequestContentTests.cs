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
using Grpc.Net.Client.Web.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests;

[TestFixture]
public class GrpcWebRequestContentTests
{
    [Test]
    public void ContentLength_InnerHasContentLength_GrpcWeb_UseValue()
    {
        // Arrange
        var testHttpContext = new TestHttpContent() { ContentLength = 10 };
        var content = new GrpcWebRequestContent(testHttpContext, GrpcWebMode.GrpcWeb);

        // Act
        var contentLength = content.Headers.ContentLength;

        // Assert
        Assert.AreEqual(10, contentLength);
    }

    [Test]
    public void ContentLength_InnerHasContentLength_GrpcWebText_UseValue()
    {
        // Arrange
        var testHttpContext = new TestHttpContent() { ContentLength = 10 };
        var content = new GrpcWebRequestContent(testHttpContext, GrpcWebMode.GrpcWebText);

        // Act
        var contentLength = content.Headers.ContentLength;

        // Assert
        Assert.AreEqual(16, contentLength);
    }

    [Test]
    public void ContentLength_InnerMissingContentLength_Null()
    {
        // Arrange
        var testHttpContext = new TestHttpContent() { ContentLength = null };
        var content = new GrpcWebRequestContent(testHttpContext, GrpcWebMode.GrpcWebText);

        // Act
        var contentLength = content.Headers.ContentLength;

        // Assert
        Assert.AreEqual(null, contentLength);
    }

    private class TestHttpContent : HttpContent
    {
        public bool Disposed { get; private set; }

        public long? ContentLength { get; set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new System.NotImplementedException();
        }

        protected override bool TryComputeLength(out long length)
        {
            if (ContentLength != null)
            {
                length = ContentLength.GetValueOrDefault();
                return true;
            }

            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
