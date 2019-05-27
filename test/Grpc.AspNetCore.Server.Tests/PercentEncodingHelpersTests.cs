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
using Grpc.AspNetCore.Server.Internal;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class PercentEncodingHelpersTests
    {
        [TestCaseSource(nameof(ValidPercentEncodingTestCases))]
        public void EncodeMessageTrailer(string value, string expectedEncodedValue)
        {
            // Arrange & Act
            var encodedValue = PercentEncodingHelpers.PercentEncode(value);

            // Assert
            Assert.AreEqual(expectedEncodedValue, encodedValue);
        }

        [TestCaseSource(nameof(ValidPercentEncodingTestCases))]
        public void DecodeMessageTrailer(string expectedDecodedValue, string value)
        {
            // Arrange & Act
            var decodedValue = Uri.UnescapeDataString(value);

            // Assert
            Assert.AreEqual(expectedDecodedValue, decodedValue);
        }

        [Test]
        public void StartOfHeaderChar_Roundtrip_Success()
        {
            // Arrange & Act
            var encodedValue = PercentEncodingHelpers.PercentEncode("\x01");
            var decodedValue = Uri.UnescapeDataString("%01");

            // Assert
            Assert.AreEqual("%01", encodedValue);
            Assert.AreEqual("\x01", decodedValue);

        }

        [Test]
        public void ShiftInChar_Roundtrip_Success()
        {
            // Arrange & Act
            var encodedValue = PercentEncodingHelpers.PercentEncode("\x0f");
            var decodedValue = Uri.UnescapeDataString("%0F");

            // Assert
            Assert.AreEqual("%0F", encodedValue);
            Assert.AreEqual("\x0f", decodedValue);
        }

        // This test double-checks that UnescapeDataString doesn't thrown when given odd data
        [TestCase("%", "%")]
        [TestCase("%A", "%A")]
        [TestCase("%A%", "%A%")]
        [TestCase("%AG", "%AG")]
        [TestCase("%G0", "%G0")]
        [TestCase("H%6", "H%6")]
        [TestCase("\0", "\0")]
        public void DecodeInvalidMessageTrailer(string expectedDecodedValue, string value)
        {
            // Arrange & Act
            var decodedValue = Uri.UnescapeDataString(value);

            // Assert
            Assert.AreEqual(expectedDecodedValue, decodedValue);
        }

        [Test]
        public void PercentEncode_UnmatchedHighSurrogate_ReplacementCharacter()
        {
            // Arrange & Act
            var escaped = PercentEncodingHelpers.PercentEncode("unmatchedHighSurrogate " + ((char)0xD801));

            // Assert
            Assert.AreEqual("unmatchedHighSurrogate %EF%BF%BD", escaped);
        }

        [Test]
        public void PercentEncode_UnmatchedHighSurrogatesFollowedByAscii_AsciiNotEncoded()
        {
            // Arrange & Act
            var escaped = PercentEncodingHelpers.PercentEncode("unmatchedHighSurrogate " + ((char)0xD801) + ((char)0xD801) + "a");

            // Assert
            Assert.AreEqual("unmatchedHighSurrogate %EF%BF%BD%EF%BF%BDa", escaped);
        }

        [Test]
        public void PercentEncode_UnmatchedLowSurrogate_ReplacementCharacter()
        {
            // Arrange & Act
            var escaped = PercentEncodingHelpers.PercentEncode("unmatchedLowSurrogate " + ((char)0xDC37));

            // Assert
            Assert.AreEqual("unmatchedLowSurrogate %EF%BF%BD", escaped);
        }

        [Test]
        public void PercentEncode_TrailingHighSurrogate_SurrogatePairCorrectlyEncoded()
        {
            // Arrange
            var originalText = "unmatchedLowSurrogate " + new string('£', PercentEncodingHelpers.MaxUnicodeCharsReallocate - 2) + "😀";

            // Act
            var escaped = PercentEncodingHelpers.PercentEncode(originalText);

            // Assert
            Assert.IsTrue(escaped.EndsWith("%F0%9F%98%80"), escaped);
            Assert.AreEqual(originalText, Uri.UnescapeDataString(escaped));
        }

        // This test allocates a very large string
        // If it breaks on some environments then feel free to remove it
        [Test]
        public void PercentEncode_LargeUnicodeString_OverflowErrorThrown()
        {
            // Arrange
            var originalText = new string('元', int.MaxValue / 8);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => PercentEncodingHelpers.PercentEncode(originalText));

            // Assert
            Assert.AreEqual("Value is too large to encode.", ex.Message);
        }

        private static TestCaseData[] ValidPercentEncodingTestCases =
        {
            new TestCaseData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~"),
            new TestCaseData("\x00", "%00"),
            new TestCaseData("a b", "a b"),
            new TestCaseData(" b", " b"),
            new TestCaseData("\xff", "%C3%BF"),
            new TestCaseData("\xee", "%C3%AE"),
            new TestCaseData("%2", "%252"),
            new TestCaseData("", ""),
            new TestCaseData("☺", "%E2%98%BA"),
            new TestCaseData("😈", "%F0%9F%98%88"),
            new TestCaseData(new string('a', 100), new string('a', 100)),
            new TestCaseData(new string('£', 100), string.Join("", Enumerable.Repeat("%C2%A3", 100))),
            new TestCaseData("££", "%C2%A3%C2%A3"),
            new TestCaseData("a£a£a", "a%C2%A3a%C2%A3a"),
            new TestCaseData("元", "%E5%85%83"),

            new TestCaseData("my favorite character is \u0000", "my favorite character is %00"),
            new TestCaseData("my favorite character is %", "my favorite character is %25"),
            new TestCaseData("my favorite character is 𐀁", "my favorite character is %F0%90%80%81"), // surrogatePair
            new TestCaseData("my favorite character is " + ((char)0xDBFF) + ((char)0xDFFF), "my favorite character is %F4%8F%BF%BF"),
            new TestCaseData("Hello", "Hello"),
            new TestCaseData("𐀁", "%F0%90%80%81"),
        };
    }
}
