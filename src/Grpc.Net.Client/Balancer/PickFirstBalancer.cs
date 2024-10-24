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
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer;

/// <summary>
/// A <see cref="LoadBalancer"/> that attempts to connect to addresses until a connection
/// is successfully made. gRPC calls are all made to the first successful connection.
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
internal sealed class PickFirstBalancer : LoadBalancer
{
    private readonly IChannelControlHelper _controller;
    private readonly ILogger _logger;

    internal Subchannel? _subchannel;
    private ConnectivityState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="PickFirstBalancer"/> class.
    /// </summary>
    /// <param name="controller">The controller.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public PickFirstBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory)
    {
        _controller = controller;
        _logger = loggerFactory.CreateLogger(typeof(PickFirstBalancer));
    }

    private void ResolverError(Status status)
    {
        // If balancer doesn't have a ready subchannel then remove any current subchannel
        // and update channel state with resolver error.
        switch (_state)
        {
            case ConnectivityState.Idle:
            case ConnectivityState.Connecting:
            case ConnectivityState.TransientFailure:
                if (_subchannel != null)
                {
                    RemoveSubchannel();
                }
                _controller.UpdateState(new BalancerState(ConnectivityState.TransientFailure, new ErrorPicker(status)));
                break;
        }
    }

    private void RemoveSubchannel()
    {
        if (_subchannel != null)
        {
            _subchannel.Dispose();
            _subchannel = null;
        }
    }

    /// <inheritdoc />
    public override void UpdateChannelState(ChannelState state)
    {
        if (state.Status.StatusCode != StatusCode.OK)
        {
            ResolverError(state.Status);
            return;
        }
        if (state.Addresses == null || state.Addresses.Count == 0)
        {
            ResolverError(new Status(StatusCode.Unavailable, "Resolver returned no addresses."));
            return;
        }

        if (_subchannel == null)
        {
            try
            {
                _subchannel = _controller.CreateSubchannel(new SubchannelOptions(state.Addresses));
                _subchannel.OnStateChanged(s => UpdateSubchannelState(_subchannel, s));
            }
            catch (Exception ex)
            {
                var picker = new ErrorPicker(new Status(StatusCode.Unavailable, "Error creating subchannel.", ex));
                _controller.UpdateState(new BalancerState(ConnectivityState.TransientFailure, picker));
                throw;
            }

            _controller.UpdateState(new BalancerState(ConnectivityState.Idle, EmptyPicker.Instance));
            _subchannel.RequestConnection();
        }
        else
        {
            _subchannel.UpdateAddresses(state.Addresses);
        }
    }

    private void UpdateSubchannelState(Subchannel subchannel, SubchannelState state)
    {
        if (_subchannel != subchannel)
        {
            PickFirstBalancerLog.IgnoredSubchannelStateChange(_logger, subchannel.Id);
            return;
        }

        PickFirstBalancerLog.ProcessingSubchannelStateChanged(_logger, subchannel.Id, state.State, state.Status);

        switch (state.State)
        {
            case ConnectivityState.Ready:
                UpdateChannelState(state.State, new PickFirstPicker(_subchannel));
                break;
            case ConnectivityState.Idle:
                _controller.RefreshResolver();

                // Pick first load balancer waits until a request is made before establishing a connection.
                // Return picker that will request a connection on pick.
                UpdateChannelState(state.State, new RequestConnectionPicker(_subchannel));
                break;
            case ConnectivityState.Connecting:
                UpdateChannelState(state.State, EmptyPicker.Instance);
                break;
            case ConnectivityState.TransientFailure:
                UpdateChannelState(state.State, new ErrorPicker(state.Status));
                break;
            case ConnectivityState.Shutdown:
                UpdateChannelState(state.State, EmptyPicker.Instance);
                _subchannel = null;
                break;
        }
    }

    private void UpdateChannelState(ConnectivityState state, SubchannelPicker subchannelPicker)
    {
        _state = state;
        _controller.UpdateState(new BalancerState(state, subchannelPicker));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        RemoveSubchannel();
    }

    /// <inheritdoc />
    public override void RequestConnection()
    {
        _subchannel?.RequestConnection();
    }
}

internal class PickFirstPicker : SubchannelPicker
{
    internal Subchannel Subchannel { get; }

    public PickFirstPicker(Subchannel subchannel)
    {
        Subchannel = subchannel;
    }

    public override PickResult Pick(PickContext context)
    {
        return PickResult.ForSubchannel(Subchannel);
    }
}

internal sealed class RequestConnectionPicker : PickFirstPicker
{
    public RequestConnectionPicker(Subchannel subchannel) : base(subchannel)
    {
    }

    public override PickResult Pick(PickContext context)
    {
        Subchannel.RequestConnection();
        return base.Pick(context);
    }
}

internal static partial class PickFirstBalancerLog
{
    [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "ProcessingSubchannelStateChanged", Message = "Processing subchannel id '{SubchannelId}' state changed to {State}. Detail: '{Detail}'.")]
    private static partial void ProcessingSubchannelStateChanged(ILogger logger, string subchannelId, ConnectivityState state, string Detail, Exception? DebugException);

    public static void ProcessingSubchannelStateChanged(ILogger logger, string subchannelId, ConnectivityState state, Status status)
    {
        ProcessingSubchannelStateChanged(logger, subchannelId, state, status.Detail, status.DebugException);
    }

    [LoggerMessage(Level = LogLevel.Trace, EventId = 2, EventName = "IgnoredSubchannelStateChange", Message = "Ignored state change because of unknown subchannel id '{SubchannelId}'.")]
    public static partial void IgnoredSubchannelStateChange(ILogger logger, string subchannelId);
}

/// <summary>
/// A <see cref="LoadBalancerFactory"/> that matches the name <c>pick_first</c>
/// and creates <see cref="PickFirstBalancer"/> instances.
/// <para>
/// Note: Experimental API that can change or be removed without any prior notice.
/// </para>
/// </summary>
public sealed class PickFirstBalancerFactory : LoadBalancerFactory
{
    /// <inheritdoc />
    public override string Name { get; } = LoadBalancingConfig.PickFirstPolicyName;

    /// <inheritdoc />
    public override LoadBalancer Create(LoadBalancerOptions options)
    {
        return new PickFirstBalancer(options.Controller, options.LoggerFactory);
    }
}
#endif
