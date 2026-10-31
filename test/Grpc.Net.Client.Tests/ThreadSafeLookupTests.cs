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

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class ThreadSafeLookupTests
{
    [Test]
    public void GetOrAdd_ReturnsCorrectValueForNewKey()
    {
        var lookup = new ThreadSafeLookup<int, string>();
        var result = lookup.GetOrAdd(1, k => "Value-1");

        Assert.AreEqual("Value-1", result);
    }

    [Test]
    public void GetOrAdd_ReturnsExistingValueForExistingKey()
    {
        var lookup = new ThreadSafeLookup<int, string>();
        lookup.GetOrAdd(1, k => "InitialValue");
        var result = lookup.GetOrAdd(1, k => "NewValue");

        Assert.AreEqual("InitialValue", result);
    }

    [Test]
    public void GetOrAdd_SwitchesToDictionaryAfterThreshold()
    {
        var addCount = (ThreadSafeLookup<int, string>.Threshold * 2);
        var lookup = new ThreadSafeLookup<int, string>();

        for (var i = 0; i <= addCount; i++)
        {
            lookup.GetOrAdd(i, k => $"Value-{k}");
        }

        var result = lookup.GetOrAdd(addCount, k => $"NewValue-{addCount}");

        Assert.AreEqual($"Value-{addCount}", result);
    }

    [Test]
    public void GetOrAdd_HandlesConcurrentAccess()
    {
        var lookup = new ThreadSafeLookup<int, string>();
        Parallel.For(0, 1000, i =>
        {
            var value = lookup.GetOrAdd(i, k => $"Value-{k}");
            Assert.AreEqual($"Value-{i}", value);
        });
    }

    [TestCase(0)]
    [TestCase(ThreadSafeLookup<string, string>.Threshold)]
    [TestCase(ThreadSafeLookup<string, string>.Threshold + 1)]
    public void GetOrAdd_CustomComparer_ReturnsExistingValue(int initialCount)
    {
        var lookup = new ThreadSafeLookup<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < initialCount; i++)
        {
            lookup.GetOrAdd($"Key-{i}", k => k);
        }

        var factoryCalls = 0;
        var value = lookup.GetOrAdd("Key", k =>
        {
            factoryCalls++;
            return "InitialValue";
        });
        var result = lookup.GetOrAdd("KEY", k =>
        {
            factoryCalls++;
            return "NewValue";
        });

        Assert.AreEqual(value, result);
        Assert.AreEqual(1, factoryCalls);
        for (var i = 0; i < initialCount; i++)
        {
            Assert.AreEqual($"Key-{i}", lookup.GetOrAdd($"KEY-{i}", k => "NewValue"));
        }
    }
}
