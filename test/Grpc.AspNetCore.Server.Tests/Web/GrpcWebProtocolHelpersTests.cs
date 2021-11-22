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

using System.Buffers;
using System.Text;
using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class GrpcWebProtocolHelpersTests
    {
        [Test]
        public void WriteTrailers_NoTrailers_WrittenToOutput()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            var output = new ArrayBufferWriter<byte>();

            // Act
            GrpcWebProtocolHelpers.WriteTrailers(trailers, output);

            // Assert
            Assert.AreEqual(5, output.WrittenSpan.Length);

            Assert.AreEqual(128, output.WrittenSpan[0]);
            Assert.AreEqual(0, output.WrittenSpan[1]);
            Assert.AreEqual(0, output.WrittenSpan[2]);
            Assert.AreEqual(0, output.WrittenSpan[3]);
            Assert.AreEqual(0, output.WrittenSpan[4]);
        }

        [Test]
        public void WriteTrailers_OneTrailer_WrittenToOutput()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            trailers.Add("one", "two");
            var output = new ArrayBufferWriter<byte>();

            // Act
            GrpcWebProtocolHelpers.WriteTrailers(trailers, output);

            // Assert
            Assert.AreEqual(15, output.WrittenSpan.Length);

            Assert.AreEqual(128, output.WrittenSpan[0]);
            Assert.AreEqual(0, output.WrittenSpan[1]);
            Assert.AreEqual(0, output.WrittenSpan[2]);
            Assert.AreEqual(0, output.WrittenSpan[3]);
            Assert.AreEqual(10, output.WrittenSpan[4]);

            var text = Encoding.ASCII.GetString(output.WrittenSpan.Slice(5));

            Assert.AreEqual("one: two\r\n", text);
        }

        [Test]
        public void WriteTrailers_OneTrailerMixedCase_WrittenToOutputLowerCase()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            trailers.Add("One", "Two");
            var output = new ArrayBufferWriter<byte>();

            // Act
            GrpcWebProtocolHelpers.WriteTrailers(trailers, output);

            // Assert
            Assert.AreEqual(15, output.WrittenSpan.Length);

            Assert.AreEqual(128, output.WrittenSpan[0]);
            Assert.AreEqual(0, output.WrittenSpan[1]);
            Assert.AreEqual(0, output.WrittenSpan[2]);
            Assert.AreEqual(0, output.WrittenSpan[3]);
            Assert.AreEqual(10, output.WrittenSpan[4]);

            var text = Encoding.ASCII.GetString(output.WrittenSpan.Slice(5));

            Assert.AreEqual("one: Two\r\n", text);
        }

        [Test]
        public void WriteTrailers_MultiValueTrailer_WrittenToOutput()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            trailers.Add("one", new StringValues(new[] { "two", "three" }));
            var output = new ArrayBufferWriter<byte>();

            // Act
            GrpcWebProtocolHelpers.WriteTrailers(trailers, output);

            // Assert
            Assert.AreEqual(27, output.WrittenSpan.Length);

            Assert.AreEqual(128, output.WrittenSpan[0]);
            Assert.AreEqual(0, output.WrittenSpan[1]);
            Assert.AreEqual(0, output.WrittenSpan[2]);
            Assert.AreEqual(0, output.WrittenSpan[3]);
            Assert.AreEqual(22, output.WrittenSpan[4]);

            var text = Encoding.ASCII.GetString(output.WrittenSpan.Slice(5));

            Assert.AreEqual("one: two\r\none: three\r\n", text);
        }

        [Test]
        public void WriteTrailers_InvalidHeaderName_Error()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            trailers.Add("one\r", "two");
            var output = new ArrayBufferWriter<byte>();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcWebProtocolHelpers.WriteTrailers(trailers, output))!;

            // Assert
            Assert.AreEqual("Invalid non-ASCII or control character in header: 0x000D", ex.Message);
        }

        [Test]
        public void WriteTrailers_InvalidHeaderValue_Error()
        {
            // Arrange
            var trailers = new HeaderDictionary();
            trailers.Add("one", "two:" + (char)127);
            var output = new ArrayBufferWriter<byte>();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcWebProtocolHelpers.WriteTrailers(trailers, output))!;

            // Assert
            Assert.AreEqual("Invalid non-ASCII or control character in header: 0x007F", ex.Message);
        }
    }
}
