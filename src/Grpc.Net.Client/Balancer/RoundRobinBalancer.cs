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
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer;

/// <summary>
/// A <see cref="LoadBalancer"/> that attempts to connect to all addresses. gRPC calls are distributed
/// across all successful connections using round-robin logic.
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
internal sealed class RoundRobinBalancer : SubchannelsLoadBalancer
{
    private readonly IRandomGenerator _randomGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundRobinBalancer"/> class.
    /// </summary>
    /// <param name="controller">The controller.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public RoundRobinBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory)
        : this(controller, loggerFactory, new RandomGenerator())
    {
    }

    internal RoundRobinBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory, IRandomGenerator randomGenerator)
        : base(controller, loggerFactory)
    {
        _randomGenerator = randomGenerator;
    }

    /// <inheritdoc />
    protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels)
    {
        var pickCount = _randomGenerator.Next(0, readySubchannels.Count);
        return new RoundRobinPicker(readySubchannels, pickCount);
    }
}

internal sealed class RoundRobinPicker : SubchannelPicker
{
    // Internal for testing
    internal readonly List<Subchannel> _subchannels;
    private long _pickCount;

    public RoundRobinPicker(IReadOnlyList<Subchannel> subchannels, long pickCount)
    {
        _subchannels = subchannels.ToList();
        _pickCount = pickCount;
    }

    public override PickResult Pick(PickContext context)
    {
        var c = Interlocked.Increment(ref _pickCount);
        var index = c % _subchannels.Count;
        var item = _subchannels[(int)index];

        return PickResult.ForSubchannel(item);
    }

    public override string ToString()
    {
        return string.Join(", ", _subchannels.Select(s => s.ToString()));
    }
}

/// <summary>
/// A <see cref="LoadBalancerFactory"/> that matches the name <c>round_robin</c>
/// and creates <see cref="RoundRobinBalancer"/> instances.
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
public sealed class RoundRobinBalancerFactory : LoadBalancerFactory
{
    /// <inheritdoc />
    public override string Name { get; } = LoadBalancingConfig.RoundRobinPolicyName;

    /// <inheritdoc />
    public override LoadBalancer Create(LoadBalancerOptions options)
    {
        return new RoundRobinBalancer(options.Controller, options.LoggerFactory);
    }
}
#endif
