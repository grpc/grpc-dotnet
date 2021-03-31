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
using System.Collections.Generic;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal.Configuration;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class ServiceConfigTests
    {
        [Test]
        public void MethodConfig_CreateUnderlyingConfig()
        {
            // Arrange & Act
            var serviceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { new MethodName() },
                        RetryPolicy = new RetryPolicy
                        {
                            MaxAttempts = 5,
                            InitialBackoff = TimeSpan.FromSeconds(1),
                            RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.Aborted }
                        }
                    }
                }
            };

            // Assert
            Assert.AreEqual(1, serviceConfig.MethodConfigs.Count);
            Assert.AreEqual(1, serviceConfig.MethodConfigs[0].Names.Count);
            Assert.AreEqual(5, serviceConfig.MethodConfigs[0].RetryPolicy!.MaxAttempts);
            Assert.AreEqual(TimeSpan.FromSeconds(1), serviceConfig.MethodConfigs[0].RetryPolicy!.InitialBackoff);
            Assert.AreEqual(StatusCode.Unavailable, serviceConfig.MethodConfigs[0].RetryPolicy!.RetryableStatusCodes[0]);
            Assert.AreEqual(StatusCode.Aborted, serviceConfig.MethodConfigs[0].RetryPolicy!.RetryableStatusCodes[1]);

            var inner = serviceConfig.Inner;
            var methodConfigs = (IList<object>)inner["methodConfig"];
            var allServices = (IDictionary<string, object>)methodConfigs[0];

            Assert.AreEqual(5, (int)((IDictionary<string, object>)allServices["retryPolicy"])["maxAttempts"]);
            Assert.AreEqual("1s", (string)((IDictionary<string, object>)allServices["retryPolicy"])["initialBackoff"]);
            Assert.AreEqual("UNAVAILABLE", (string)((IList<object>)((IDictionary<string, object>)allServices["retryPolicy"])["retryableStatusCodes"])[0]);
            Assert.AreEqual("ABORTED", (string)((IList<object>)((IDictionary<string, object>)allServices["retryPolicy"])["retryableStatusCodes"])[1]);
        }

        [Test]
        public void MethodConfig_ReadUnderlyingConfig()
        {
            // Arrange
            var inner = new Dictionary<string, object>
            {
                ["methodConfig"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = new List<object> { new Dictionary<string, object>() },
                        ["retryPolicy"] = new Dictionary<string, object>
                        {
                            ["maxAttempts"] = 5,
                            ["initialBackoff"] = "1s",
                            ["retryableStatusCodes"] = new List<object> { "UNAVAILABLE", "ABORTED" }
                        }
                    }
                }
            };

            // Act
            var serviceConfig = new ServiceConfig(inner);

            // Assert
            Assert.AreEqual(1, serviceConfig.MethodConfigs.Count);
            Assert.AreEqual(1, serviceConfig.MethodConfigs[0].Names.Count);
            Assert.AreEqual(5, serviceConfig.MethodConfigs[0].RetryPolicy!.MaxAttempts);
            Assert.AreEqual(TimeSpan.FromSeconds(1), serviceConfig.MethodConfigs[0].RetryPolicy!.InitialBackoff);
            Assert.AreEqual(StatusCode.Unavailable, serviceConfig.MethodConfigs[0].RetryPolicy!.RetryableStatusCodes[0]);
            Assert.AreEqual(StatusCode.Aborted, serviceConfig.MethodConfigs[0].RetryPolicy!.RetryableStatusCodes[1]);
        }

        [Test]
        public void LoadBalancingConfig_CreateUnderlyingConfig()
        {
            // Arrange & Act
            var serviceConfig = new ServiceConfig
            {
                LoadBalancingConfigs =
                {
                    new RoundRobinConfig(),
                    new PickFirstConfig()
                }
            };

            // Assert
            Assert.AreEqual(2, serviceConfig.LoadBalancingConfigs.Count);
            Assert.AreEqual(LoadBalancingConfig.RoundRobinPolicyName, serviceConfig.LoadBalancingConfigs[0].PolicyName);
            Assert.AreEqual(LoadBalancingConfig.PickFirstPolicyName, serviceConfig.LoadBalancingConfigs[1].PolicyName);

            var inner = serviceConfig.Inner;
            var loadBalancingConfigs = (IList<object>)inner["loadBalancingConfig"];
            var roundRobinConfig = (IDictionary<string, object>)loadBalancingConfigs[0];
            var pickFirstConfig = (IDictionary<string, object>)loadBalancingConfigs[1];

            Assert.IsNotNull((IDictionary<string, object>)roundRobinConfig[LoadBalancingConfig.RoundRobinPolicyName]);
            Assert.IsNotNull((IDictionary<string, object>)pickFirstConfig[LoadBalancingConfig.PickFirstPolicyName]);
        }

        [Test]
        public void LoadBalancingConfig_ReadUnderlyingConfig()
        {
            // Arrange
            var inner = new Dictionary<string, object>
            {
                ["loadBalancingConfig"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["round_robin"] = new Dictionary<string, object> { }
                    },
                    new Dictionary<string, object>
                    {
                        ["pick_first"] = new Dictionary<string, object> { }
                    }
                }
            };

            // Act
            var serviceConfig = new ServiceConfig(inner);

            // Assert
            Assert.AreEqual(2, serviceConfig.LoadBalancingConfigs.Count);
            Assert.IsInstanceOf(typeof(RoundRobinConfig), serviceConfig.LoadBalancingConfigs[0]);
            Assert.AreEqual(LoadBalancingConfig.RoundRobinPolicyName, serviceConfig.LoadBalancingConfigs[0].PolicyName);
            Assert.IsInstanceOf(typeof(PickFirstConfig), serviceConfig.LoadBalancingConfigs[1]);
            Assert.AreEqual(LoadBalancingConfig.PickFirstPolicyName, serviceConfig.LoadBalancingConfigs[1].PolicyName);
        }

        [Test]
        public void RetryThrottlingPolicy_ReadUnderlyingConfig_Success()
        {
            // Arrange
            var inner = new Dictionary<string, object>
            {
                ["initialBackoff"] = "1.1s",
                ["retryableStatusCodes"] = new List<object> { "UNAVAILABLE", "Aborted", 1 }
            };

            // Act
            var retryPolicy = new RetryPolicy(inner);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(1.1), retryPolicy.InitialBackoff);
            Assert.AreEqual(StatusCode.Unavailable, retryPolicy.RetryableStatusCodes[0]);
            Assert.AreEqual(StatusCode.Aborted, retryPolicy.RetryableStatusCodes[1]);
            Assert.AreEqual(StatusCode.Cancelled, retryPolicy.RetryableStatusCodes[2]);
        }

        [TestCase("0s", 0)]
        [TestCase("0.0s", 0)]
        [TestCase("-0s", 0)]
        [TestCase("1s", 1 * TimeSpan.TicksPerSecond)]
        [TestCase("1.0s", 1 * TimeSpan.TicksPerSecond)]
        [TestCase("1.1s", (long)(1.1 * TimeSpan.TicksPerSecond))]
        [TestCase("-1s", -1 * TimeSpan.TicksPerSecond)]
        [TestCase("3.0000001s", (long)(3.0000001 * TimeSpan.TicksPerSecond))]
        [TestCase("315576000000s", (315576000000 * TimeSpan.TicksPerSecond))]
        [TestCase("-315576000000s", (-315576000000 * TimeSpan.TicksPerSecond))]
        public void ConvertDurationText_Success(string text, long ticks)
        {
            // Arrange & Act
            var timespan = ConvertHelpers.ConvertDurationText(text);

            // Assert
            Assert.AreEqual(ticks, timespan!.Value.Ticks);
        }

        [TestCase("0s", null)]
        [TestCase("0.0s", "0s")]
        [TestCase("-0s", "0s")]
        [TestCase("1s", null)]
        [TestCase("1.0s", "1s")]
        [TestCase("1.1s", null)]
        [TestCase("-1s", null)]
        [TestCase("3.0000001s", null)]
        [TestCase("315576000000s", null)]
        [TestCase("-315576000000s", null)]
        public void Duration_Roundtrip(string text, string explicitResult)
        {
            // Arrange & Act
            var timespan = ConvertHelpers.ConvertDurationText(text);

            // Assert
            Assert.AreEqual(explicitResult ?? text, ConvertHelpers.ToDurationText(timespan));
        }

        [TestCase("")]
        [TestCase("s")]
        [TestCase("0")]
        [TestCase("-")]
        [TestCase("1xs")]
        [TestCase("1,1s")]
        [TestCase("1.2345678e7")]
        [TestCase("1.2345678e7s")]
        public void ConvertDurationText_Failure(string text)
        {
            // Arrange & Act
            var ex = Assert.Throws<FormatException>(() => ConvertHelpers.ConvertDurationText(text))!;

            // Assert
            Assert.AreEqual($"'{text}' isn't a valid duration.", ex.Message);
        }

        [Test]
        public void MethodName_Default_ErrorOnChange()
        {
            // Arrange & Act & Assert
            Assert.Throws<NotSupportedException>(() => MethodName.Default.Method = "This will break");
        }
    }
}
