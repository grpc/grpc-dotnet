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

using Grpc.Net.Client.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcProtocolHelpersTests
    {
        private const int MillisecondsPerSecond = 1000;

        [TestCase(-1, "1n")]
        [TestCase(-10, "1n")]
        [TestCase(1, "1m")]
        [TestCase(10, "10m")]
        [TestCase(100, "100m")]
        [TestCase(890, "890m")]
        [TestCase(900, "900m")]
        [TestCase(901, "901m")]
        [TestCase(1000, "1S")]
        [TestCase(2000, "2S")]
        [TestCase(2500, "2500m")]
        [TestCase(59900, "59900m")]
        [TestCase(50000, "50S")]
        [TestCase(59000, "59S")]
        [TestCase(60000, "1M")]
        [TestCase(80000, "80S")]
        [TestCase(90000, "90S")]
        [TestCase(120000, "2M")]
        [TestCase(20 * 60 * MillisecondsPerSecond, "20M")]
        [TestCase(60 * 60 * MillisecondsPerSecond, "1H")]
        [TestCase(10 * 60 * 60 * MillisecondsPerSecond, "10H")]
        public void EncodeTimeout(int milliseconds, string expected)
        {
            var encoded = GrpcProtocolHelpers.EncodeTimeout(milliseconds);
            Assert.AreEqual(expected, encoded);
        }
    }
}
