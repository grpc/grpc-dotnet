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

#if SUPPORT_LOAD_BALANCING
using System;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Testing;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Collections.Generic;
using Grpc.Net.Client.Balancer.Internal;
using System.IO;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Net.Client.Balancer;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class BalancerAttributesTests
{
    [Test]
    public void TryGetValue_NotFound_ReturnFalse()
    {
        // Arrange
        var key = new BalancerAttributesKey<string>("key");

        var attributes = new BalancerAttributes();

        // Act & Assert
        Assert.False(attributes.TryGetValue(key, out var value));
        Assert.Null(value);
    }

    [Test]
    public void TryGetValue_Found_ReturnTrue()
    {
        // Arrange
        var key = new BalancerAttributesKey<string>("key");

        var attributes = new BalancerAttributes();

        attributes.Set(key, "value");

        // Act & Assert
        Assert.True(attributes.TryGetValue(key, out var value));
        Assert.AreEqual("value", value);
    }

    [Test]
    public void Remove_NotFound_ReturnFalse()
    {
        // Arrange
        var key = new BalancerAttributesKey<string>("key");

        var attributes = new BalancerAttributes();

        // Act & Assert
        Assert.False(attributes.Remove(key));
    }

    [Test]
    public void Remove_Found_ReturnTrue()
    {
        // Arrange
        var key = new BalancerAttributesKey<string>("key");

        var attributes = new BalancerAttributes();

        attributes.Set(key, "value");

        // Act & Assert
        Assert.True(attributes.Remove(key));

        Assert.False(attributes.TryGetValue(key, out var value));
        Assert.Null(value);
    }

    [Test]
    public void Set_Null_NotRemoved()
    {
        // Arrange
        var key = new BalancerAttributesKey<string?>("key");

        var attributes = new BalancerAttributes();

        // Act
        attributes.Set(key, "value");
        // Sets value to a null value but still in collection
        attributes.Set(key, null);

        // Assert
        Assert.True(attributes.Remove(key));
    }
}

#endif
