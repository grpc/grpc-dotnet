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
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Grpc.Net.Client.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests
{
    [TestFixture]
    public class GrpcWebResponseStreamTests
    {
        [Test]
        public async Task ReadAsync_EmptyMessage_ParseMessageAndTrailers()
        {
            // Arrange
            var data = Convert.FromBase64String("AAAAAACAAAAAEA0KZ3JwYy1zdGF0dXM6IDA=");
            var trailingHeaders = new TestHttpHeaders();
            var ms = new MemoryStream(data);
            var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

            // Act 1
            var contentHeaderData = new byte[5];
            var read1 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 1
            Assert.AreEqual(5, read1);
            Assert.AreEqual(0, contentHeaderData[0]);
            Assert.AreEqual(0, contentHeaderData[1]);
            Assert.AreEqual(0, contentHeaderData[2]);
            Assert.AreEqual(0, contentHeaderData[3]);
            Assert.AreEqual(0, contentHeaderData[4]);

            // Act 2
            var read2 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 2
            Assert.AreEqual(0, read2);
            Assert.AreEqual(1, trailingHeaders.Count());
            Assert.AreEqual("0", trailingHeaders.GetValues("grpc-status").Single());
        }

        [Test]
        public async Task ReadAsync_EmptyMessageAndTrailers_ParseMessageAndTrailers()
        {
            // Arrange
            var data = new byte[] { 0, 0, 0, 0, 0, 128, 0, 0, 0, 0 };
            var trailingHeaders = new TestHttpHeaders();
            var ms = new MemoryStream(data);
            var responseStream = new GrpcWebResponseStream(ms, trailingHeaders);

            // Act 1
            var contentHeaderData = new byte[5];
            var read1 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 1
            Assert.AreEqual(5, read1);
            Assert.AreEqual(0, contentHeaderData[0]);
            Assert.AreEqual(0, contentHeaderData[1]);
            Assert.AreEqual(0, contentHeaderData[2]);
            Assert.AreEqual(0, contentHeaderData[3]);
            Assert.AreEqual(0, contentHeaderData[4]);

            // Act 2
            var read2 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 2
            Assert.AreEqual(0, read2);
            Assert.AreEqual(0, trailingHeaders.Count());
        }

        [Test]
        public async Task ReadAsync_ReadContentWithLargeBuffer_ParseMessageAndTrailers()
        {
            // Arrange
            var data = new byte[] { 0, 0, 0, 0, 1, 99, 128, 0, 0, 0, 0 };
            var ms = new MemoryStream(data);
            var responseStream = new GrpcWebResponseStream(ms, new TestHttpHeaders());

            // Act 1
            var contentHeaderData = new byte[1024];
            var read1 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 1
            Assert.AreEqual(5, read1);
            Assert.AreEqual(0, contentHeaderData[0]);
            Assert.AreEqual(0, contentHeaderData[1]);
            Assert.AreEqual(0, contentHeaderData[2]);
            Assert.AreEqual(0, contentHeaderData[3]);
            Assert.AreEqual(1, contentHeaderData[4]);

            // Act 2
            var read2 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 2
            Assert.AreEqual(1, read2);

            // Act 2
            var read3 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 2
            Assert.AreEqual(0, read3);
        }

        [Test]
        public async Task ReadAsync_HasContentAfterTrailers_Errors()
        {
            // Arrange
            var data = new byte[] { 0, 0, 0, 0, 0, 128, 0, 0, 0, 0, 1 };
            var ms = new MemoryStream(data);
            var responseStream = new GrpcWebResponseStream(ms, new TestHttpHeaders());

            // Act 1
            var contentHeaderData = new byte[5];
            var read1 = await responseStream.ReadAsync(contentHeaderData);

            // Assert 1
            Assert.AreEqual(5, read1);
            Assert.AreEqual(0, contentHeaderData[0]);
            Assert.AreEqual(0, contentHeaderData[1]);
            Assert.AreEqual(0, contentHeaderData[2]);
            Assert.AreEqual(0, contentHeaderData[3]);
            Assert.AreEqual(0, contentHeaderData[4]);

            // Act 2
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => responseStream.ReadAsync(contentHeaderData).AsTask());

            // Assert 2
            Assert.AreEqual("Unexpected data after trailers.", ex.Message);
        }

        private class TestHttpHeaders : HttpHeaders
        {
        }

        [TestCase("", "")]
        [TestCase(" ", "")]
        [TestCase(" a ", "a")]
        [TestCase("      ", "")]
        [TestCase("a      ", "a")]
        [TestCase("      a", "a")]
        [TestCase("a     a", "a     a")]
        [TestCase("  a     a  ", "a     a")]
        public void Trim(string initial, string expected)
        {
            var result = GrpcWebResponseStream.Trim(Encoding.UTF8.GetBytes(initial));
            var s = Encoding.UTF8.GetString(result);
            Assert.AreEqual(expected, s);
        }
    }
}
