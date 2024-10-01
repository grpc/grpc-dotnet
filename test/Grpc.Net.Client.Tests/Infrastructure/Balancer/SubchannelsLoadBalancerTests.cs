#if SUPPORT_LOAD_BALANCING

using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Grpc.Net.Client.Tests.Infrastructure.Balancer;

public class SubchannelsLoadBalancerTests
{
    [Fact]
    public void UpdateChannelState_Should_Update_channel_state_with_different_attributes_only()
    {
        /* Arrange */

        const string host1 = "127.0.0.1";
        const string host2 = "127.0.0.2";
        const int port = 80;

        const string attributeKey = "key1";

        var controller = new CustomChannelControlHelper();
        var balancer = new CustomBalancer(controller, NullLoggerFactory.Instance);

        // create 2 addresses with some attributes
        var address1 = new BalancerAddress(host1, port);
        address1.Attributes.TryAdd(attributeKey, 20); // <-- difference

        var address2 = new BalancerAddress(host2, port);
        address2.Attributes.TryAdd(attributeKey, 80); // <-- difference

        var state1 = new ChannelState(
            status:              new Status(),
            addresses:           [address1, address2],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        // create 2 addresses with the same hosts and ports as previous but with other attribute values
        var address3 = new BalancerAddress(host1, port);
        address3.Attributes.TryAdd(attributeKey, 40); // <-- difference

        var address4 = new BalancerAddress(host2, port);
        address4.Attributes.TryAdd(attributeKey, 60); // <-- difference

        var state2 = new ChannelState(
            status:              new Status(),
            addresses:           [address3, address4],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        /* Act */

        // first update with `address1` and `address2`
        balancer.UpdateChannelState(state1);

        // remember count of `IChannelControlHelper.UpdateState()` calls
        var updateStateCallsCount1 = controller.UpdateStateCallsCount;

        // second update with `address3` and `address4`
        // which differs from `address1` and `address2` _only_ in attributes values
        balancer.UpdateChannelState(state2);

        // get count of `IChannelControlHelper.UpdateState()` calls after second update
        var updateStateCallsCount2 = controller.UpdateStateCallsCount;

        Assert.True(
            updateStateCallsCount2 > updateStateCallsCount1,
            "`IChannelControlHelper.UpdateState()` was not called from `SubchannelsLoadBalancer.UpdateChannelState()`");
    }

    [Fact]
    public void UpdateChannelState_Should_Update_channel_state_with_shorter_address_list()
    {
        /* Arrange */

        const string host1 = "127.0.0.1";
        const string host2 = "127.0.0.2";
        const int port = 80;

        const string attributeKey = "key1";

        var controller = new CustomChannelControlHelper();
        var balancer = new CustomBalancer(controller, NullLoggerFactory.Instance);

        // create 2 addresses with some attributes
        var address1 = new BalancerAddress(host1, port);
        address1.Attributes.TryAdd(attributeKey, 20); // <-- difference

        var address2 = new BalancerAddress(host2, port);
        address2.Attributes.TryAdd(attributeKey, 80); // <-- difference

        var state1 = new ChannelState(
            status:              new Status(),
            addresses:           [address1, address2],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        // create 1 address with the same host and port as one of the previous addresses but with other attribute value
        var address3 = new BalancerAddress(host1, port);
        address3.Attributes.TryAdd(attributeKey, 40); // <-- difference

        var state2 = new ChannelState(
            status:              new Status(),
            addresses:           [address3],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        /* Act */

        // first update with `address1` and `address2`
        balancer.UpdateChannelState(state1);

        // remember count of `IChannelControlHelper.UpdateState()` calls
        var updateStateCallsCount1 = controller.UpdateStateCallsCount;

        // second update with `address3` and `address4`
        // which differs from `address1` and `address2` _only_ in attributes values
        balancer.UpdateChannelState(state2);

        // get count of `IChannelControlHelper.UpdateState()` calls after second update
        var updateStateCallsCount2 = controller.UpdateStateCallsCount;

        Assert.True(
            updateStateCallsCount2 > updateStateCallsCount1,
            "`IChannelControlHelper.UpdateState()` was not called from `SubchannelsLoadBalancer.UpdateChannelState()`");
    }

    [Fact]
    public void UpdateChannelState_Should_Update_channel_state_with_longer_address_list()
    {
        /* Arrange */

        const string host1 = "127.0.0.1";
        const string host2 = "127.0.0.2";
        const int port = 80;

        const string attributeKey = "key1";
        const string attributeKey2 = "key2";

        var controller = new CustomChannelControlHelper();
        var balancer = new CustomBalancer(controller, NullLoggerFactory.Instance);

        // create 2 addresses with some attributes
        var address1 = new BalancerAddress(host1, port);
        address1.Attributes.TryAdd(attributeKey, 20); // <-- difference

        var address2 = new BalancerAddress(host2, port);
        address2.Attributes.TryAdd(attributeKey, 80); // <-- difference

        var state1 = new ChannelState(
            status:              new Status(),
            addresses:           [address1, address2],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        // create 2 addresses with the same hosts and ports as previous but with other attribute values
        var address3 = new BalancerAddress(host1, port);
        address3.Attributes.TryAdd(attributeKey, 40); // <-- difference

        var address4 = new BalancerAddress(host2, port);
        address4.Attributes.TryAdd(attributeKey, 60); // <-- difference

        // create one extra address with another fake attribute
        var address5 = new BalancerAddress(host2, port);
        address5.Attributes.TryAdd(attributeKey, 60);
        address5.Attributes.TryAdd(attributeKey2, "Fake"); // <-- difference

        var state2 = new ChannelState(
            status:              new Status(),
            addresses:           [address3, address4, address5],
            loadBalancingConfig: null,
            attributes:          new BalancerAttributes());

        /* Act */

        // first update with `address1` and `address2`
        balancer.UpdateChannelState(state1);

        // remember count of `IChannelControlHelper.UpdateState()` calls
        var updateStateCallsCount1 = controller.UpdateStateCallsCount;

        // second update with `address3` and `address4`
        // which differs from `address1` and `address2` _only_ in attributes values
        balancer.UpdateChannelState(state2);

        // get count of `IChannelControlHelper.UpdateState()` calls after second update
        var updateStateCallsCount2 = controller.UpdateStateCallsCount;

        Assert.True(
            updateStateCallsCount2 > updateStateCallsCount1,
            "`IChannelControlHelper.UpdateState()` was not called from `SubchannelsLoadBalancer.UpdateChannelState()`");
    }
}

/* Helper chasses */
file class CustomBalancer(
    IChannelControlHelper controller,
    ILoggerFactory loggerFactory)
    : SubchannelsLoadBalancer(controller, loggerFactory)
{
    protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels)
    {
        return new CustomPicker(readySubchannels);
    }
}

file class CustomChannelControlHelper : IChannelControlHelper
{
    public int UpdateStateCallsCount { get; private set; }

    public Subchannel CreateSubchannel(SubchannelOptions options)
    {
        var subchannelTransportFactory = new CustomSubchannelTransportFactory();

        var manager = new ConnectionManager(
            new CustomResolver(),
            true,
            NullLoggerFactory.Instance,
            new CustomBackoffPolicyFactory(),
            subchannelTransportFactory,
            []);

        return ((IChannelControlHelper)manager).CreateSubchannel(options);
    }

    public void UpdateState(BalancerState state)
    {
        UpdateStateCallsCount++;
    }

    public void RefreshResolver() { }
}

file class CustomResolver() : PollingResolver(NullLoggerFactory.Instance)
{
    protected override Task ResolveAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

file class CustomBackoffPolicyFactory : IBackoffPolicyFactory
{
    public IBackoffPolicy Create()
    {
        return new CustomBackoffPolicy();
    }
}

file class CustomBackoffPolicy : IBackoffPolicy
{
    public TimeSpan NextBackoff()
    {
        return TimeSpan.Zero;
    }
}

file class CustomSubchannelTransportFactory: ISubchannelTransportFactory
{
    public ISubchannelTransport Create(Subchannel subchannel)
    {
        return new CustomSubchannelTransport();
    }
}

file class CustomSubchannelTransport : ISubchannelTransport
{
    public void Dispose() { }

    public DnsEndPoint? CurrentEndPoint { get; }
    public TimeSpan? ConnectTimeout { get; }
    public TransportStatus TransportStatus { get; }

    public ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<Stream>(new MemoryStream());
    }

    public ValueTask<ConnectResult> TryConnectAsync(ConnectContext context, int attempt)
    {
        return ValueTask.FromResult(ConnectResult.Success);
    }

    public void Disconnect() { }
}

#endif
