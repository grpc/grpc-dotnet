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
using Grpc.Net.Client.Balancer;

namespace Frontend.Balancer
{
    public class ReportingLoadBalancerFactory : LoadBalancerFactory
    {
        private readonly LoadBalancerFactory _loadBalancerFactory;
        private readonly SubchannelReporter _subchannelReporter;

        public ReportingLoadBalancerFactory(LoadBalancerFactory loadBalancerFactory, SubchannelReporter subchannelReporter)
        {
            _loadBalancerFactory = loadBalancerFactory;
            _subchannelReporter = subchannelReporter;
        }

        public override string Name => _loadBalancerFactory.Name;

        public override LoadBalancer Create(LoadBalancerOptions options)
        {
            // Wrap the helper so that state updates can be intercepted.
            // State information is then passed to the reporter.
            var reportingController = new ReportingChannelControlHelper(options.Controller, _subchannelReporter);

            options = new LoadBalancerOptions(reportingController, options.LoggerFactory, options.Configuration);
            return _loadBalancerFactory.Create(options);
        }
    }
}
