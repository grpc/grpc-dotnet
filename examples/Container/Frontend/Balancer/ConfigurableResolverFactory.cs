﻿#region Copyright notice and license

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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;

namespace Frontend.Balancer
{
    public class ConfigurableResolverFactory : ResolverFactory
    {
        private readonly ResolverFactory _innerResolverFactory;
        private readonly BalancerConfiguration _balancerConfiguration;

        public ConfigurableResolverFactory(ResolverFactory innerResolverFactory, BalancerConfiguration balancerConfiguration)
        {
            _innerResolverFactory = innerResolverFactory;
            _balancerConfiguration = balancerConfiguration;
        }

        public override string Name => _innerResolverFactory.Name;

        public override Resolver Create(Uri address, ResolverOptions options)
        {
            return new ConfigurableResolver(_innerResolverFactory.Create(address, options), _balancerConfiguration);
        }

        private class ConfigurableResolver : Resolver
        {
            private readonly Resolver _innerResolver;
            private readonly BalancerConfiguration _balancerConfiguration;

            public ConfigurableResolver(Resolver innerResolver, BalancerConfiguration balancerConfiguration)
            {
                _innerResolver = innerResolver;
                _balancerConfiguration = balancerConfiguration;
                _balancerConfiguration.Updated += OnConfigurationUpdated;
            }

            private void OnConfigurationUpdated(object? sender, EventArgs e)
            {
                _ = RefreshAsync(CancellationToken.None);
            }

            public override Task RefreshAsync(CancellationToken cancellationToken)
            {
                return _innerResolver.RefreshAsync(cancellationToken);
            }

            public override void Start(Action<ResolverResult> listener)
            {
                _innerResolver.Start(result =>
                {
                    var policyName = _balancerConfiguration.LoadBalancerPolicyName switch
                    {
                        LoadBalancerName.PickFirst => "pick_first",
                        LoadBalancerName.RoundRobin => "round_robin",
                        _ => throw new InvalidOperationException("Unexpected load balancer.")
                    };

                    var serviceConfig = new ServiceConfig
                    {
                        LoadBalancingConfigs = { new LoadBalancingConfig(policyName) }
                    };

                    // DNS results change order between refreshes.
                    // Explicitly order by host to keep result order consistent.
                    var orderedAddresses = result.Addresses!.OrderBy(a => a.Host).ToList();
                    listener(ResolverResult.ForResult(orderedAddresses, serviceConfig));
                });
            }
        }
    }
}