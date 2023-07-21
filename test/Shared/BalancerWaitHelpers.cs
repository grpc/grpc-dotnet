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
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Tests.Shared;

internal static class BalancerWaitHelpers
{
    public static Task WaitForChannelStateAsync(ILogger logger, GrpcChannel channel, ConnectivityState state, int channelId = 1)
    {
        return WaitForChannelStatesAsync(logger, channel, new[] { state }, channelId);
    }

    public static async Task WaitForChannelStatesAsync(ILogger logger, GrpcChannel channel, ConnectivityState[] states, int channelId = 1)
    {
        var statesText = string.Join(", ", states.Select(s => $"'{s}'"));
        logger.LogInformation($"Channel id {channelId}: Waiting for channel states {statesText}.");

        var currentState = channel.State;

        while (!states.Contains(currentState))
        {
            logger.LogInformation($"Channel id {channelId}: Current channel state '{currentState}' doesn't match expected states {statesText}.");

            await channel.WaitForStateChangedAsync(currentState);
            currentState = channel.State;
        }

        logger.LogInformation($"Channel id {channelId}: Current channel state '{currentState}' matches expected states {statesText}.");
    }

    public static async Task<Subchannel> WaitForSubchannelToBeReadyAsync(ILogger logger, GrpcChannel channel, Func<SubchannelPicker?, Subchannel[]>? getPickerSubchannels = null, Func<Subchannel, bool>? validateSubchannel = null)
    {
        var subChannel = (await WaitForSubchannelsToBeReadyAsync(logger, channel, expectedCount: 1, getPickerSubchannels, validateSubchannel)).Single();
        return subChannel;
    }

    public static Task<Subchannel[]> WaitForSubchannelsToBeReadyAsync(ILogger logger, GrpcChannel channel, int expectedCount, Func<SubchannelPicker?, Subchannel[]>? getPickerSubchannels = null, Func<Subchannel, bool>? validateSubchannel = null)
    {
        return WaitForSubchannelsToBeReadyAsync(logger, channel.ConnectionManager, expectedCount, getPickerSubchannels, validateSubchannel);
    }

    public static async Task<Subchannel[]> WaitForSubchannelsToBeReadyAsync(ILogger logger, ConnectionManager connectionManager, int expectedCount, Func<SubchannelPicker?, Subchannel[]>? getPickerSubchannels = null, Func<Subchannel, bool>? validateSubchannel = null)
    {
        if (getPickerSubchannels == null)
        {
            getPickerSubchannels = (picker) =>
            {
                return picker switch
                {
                    RoundRobinPicker roundRobinPicker => roundRobinPicker._subchannels.ToArray(),
                    PickFirstPicker pickFirstPicker => new[] { pickFirstPicker.Subchannel },
                    EmptyPicker emptyPicker => Array.Empty<Subchannel>(),
                    ErrorPicker errorPicker => Array.Empty<Subchannel>(),
                    null => Array.Empty<Subchannel>(),
                    _ => throw new Exception("Unexpected picker type: " + picker.GetType().FullName)
                };
            };
        }

        logger.LogInformation($"Waiting for subchannel ready count: {expectedCount}");

        Subchannel[]? subChannelsCopy = null;
        await TestHelpers.AssertIsTrueRetryAsync(() =>
        {
            var picker = connectionManager._picker;
            subChannelsCopy = getPickerSubchannels(picker);
            logger.LogInformation($"Current subchannel ready count: {subChannelsCopy.Length}");
            for (var i = 0; i < subChannelsCopy.Length; i++)
            {
                var c = subChannelsCopy[i];
                if (validateSubchannel != null)
                {
                    var validationResult = validateSubchannel(c);
                    logger.LogInformation($"Validation result for subchannel '{c}': {validationResult}");
                    if (!validationResult)
                    {
                        logger.LogInformation("Returning false because of validation failure.");
                        return false;
                    }
                }
                logger.LogInformation($"Ready subchannel: {c}");
            }

            return subChannelsCopy.Length == expectedCount;
        }, "Wait for all subconnections to be connected.");

        logger.LogInformation($"Finished waiting for subchannel ready.");

        Debug.Assert(subChannelsCopy != null);
        return subChannelsCopy;
    }

}
#endif
