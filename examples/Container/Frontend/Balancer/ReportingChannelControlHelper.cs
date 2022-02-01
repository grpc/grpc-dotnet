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

using System.Collections.Generic;
using Grpc.Core;
using Grpc.Net.Client.Balancer;

namespace Frontend.Balancer
{
    public record ReportedSubchannelState(Subchannel Subchannel, ConnectivityState State);

    public class ReportingChannelControlHelper : IChannelControlHelper
    {
        private readonly IChannelControlHelper _controller;
        private readonly SubchannelReporter _subchannelReporter;
        private readonly List<ReportedSubchannelState> _subchannels;

        private ConnectivityState _state;

        public ReportingChannelControlHelper(
            IChannelControlHelper controller,
            SubchannelReporter subchannelReporter)
        {
            _controller = controller;
            _subchannels = new List<ReportedSubchannelState>();
            _subchannelReporter = subchannelReporter;
        }

        public Subchannel CreateSubchannel(SubchannelOptions options)
        {
            var subchannel = _controller.CreateSubchannel(options);
            subchannel.OnStateChanged(s => OnSubchannelStateChanged(subchannel, s));
            _subchannels.Add(new ReportedSubchannelState(subchannel, ConnectivityState.Idle));

            NotifySubscribers();

            return subchannel;
        }

        private void OnSubchannelStateChanged(Subchannel subchannel, SubchannelState s)
        {
            var i = _subchannels.FindIndex(s => s.Subchannel == subchannel);
            if (i >= 0)
            {
                if (s.State == ConnectivityState.Shutdown)
                {
                    _subchannels.RemoveAt(i);
                }
                else
                {
                    _subchannels[i] = new ReportedSubchannelState(subchannel, s.State);
                }
            }
        }

        public void UpdateState(BalancerState state)
        {
            _controller.UpdateState(state);
            _state = state.ConnectivityState;

            NotifySubscribers();
        }

        private void NotifySubscribers()
        {
            _subchannelReporter.NotifySubscribers(new SubchannelReporterResult(_state, _subchannels));
        }

        public void RefreshResolver()
        {
            _controller.RefreshResolver();
        }
    }
}
