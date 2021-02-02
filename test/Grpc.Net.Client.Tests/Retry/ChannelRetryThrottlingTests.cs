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

using Grpc.Net.Client.Internal.Retry;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Retry
{
    [TestFixture]
    public class ChannelRetryThrottlingTests
    {
        [Test]
        public void IsRetryThrottlingActive_FailedAndSuccessCalls_ActivatedChanges()
        {
            var channelRetryThrottling = new ChannelRetryThrottling(maxTokens: 3, tokenRatio: 1.0, NullLoggerFactory.Instance);

            Assert.AreEqual(false, channelRetryThrottling.IsRetryThrottlingActive());

            channelRetryThrottling.CallFailure();
            Assert.AreEqual(false, channelRetryThrottling.IsRetryThrottlingActive());

            channelRetryThrottling.CallFailure();
            Assert.AreEqual(true, channelRetryThrottling.IsRetryThrottlingActive());

            channelRetryThrottling.CallSuccess();
            Assert.AreEqual(false, channelRetryThrottling.IsRetryThrottlingActive());
        }
    }
}
