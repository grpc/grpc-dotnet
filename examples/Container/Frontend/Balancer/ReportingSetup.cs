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

using Grpc.Net.Client.Balancer;

namespace Frontend.Balancer
{
    public static class ReportingSetup
    {
        public static void RegisterReportingServices(IServiceCollection services)
        {
            // These services allow the load balancer policy to be configured and subchannels to be reported in the UI.
            services.AddSingleton<SubchannelReporter>();
            services.AddSingleton<BalancerConfiguration>();
            services.AddSingleton<ResolverFactory>(s =>
            {
                var inner = new DnsResolverFactory(refreshInterval: TimeSpan.FromSeconds(20));
                return new ConfigurableResolverFactory(inner, s.GetRequiredService<BalancerConfiguration>());
            });
            services.AddSingleton<LoadBalancerFactory>(s =>
            {
                var inner = new RoundRobinBalancerFactory();
                return new ReportingLoadBalancerFactory(inner, s.GetRequiredService<SubchannelReporter>());
            });
            services.AddSingleton<LoadBalancerFactory>(s =>
            {
                var inner = new PickFirstBalancerFactory();
                return new ReportingLoadBalancerFactory(inner, s.GetRequiredService<SubchannelReporter>());
            });
        }
    }
}
