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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
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

        public override Resolver Create(ResolverOptions options)
        {
            return new ConfigurableResolver(options.LoggerFactory, _innerResolverFactory.Create(options), _balancerConfiguration);
        }

        private class ConfigurableResolver : Resolver
        {
            private readonly Resolver _innerResolver;
            private readonly BalancerConfiguration _balancerConfiguration;

            private ResolverResult? _lastResult;

            public ConfigurableResolver(ILoggerFactory loggerFactory, Resolver innerResolver, BalancerConfiguration balancerConfiguration)
                : base(loggerFactory)
            {
                _innerResolver = innerResolver;
                _balancerConfiguration = balancerConfiguration;
                _balancerConfiguration.Updated += OnConfigurationUpdated;
            }

            private void OnConfigurationUpdated(object? sender, EventArgs e)
            {
                // Can't just call RefreshAsync and get new results because of rate limiting.
                if (Listener != null && _lastResult != null)
                {
                    RaiseResult(_lastResult);
                }
            }

            protected override Task ResolveAsync(CancellationToken cancellationToken)
            {
                _innerResolver.Refresh();
                return Task.CompletedTask;
            }

            protected override void OnStarted()
            {
                _innerResolver.Start(result =>
                {
                    _lastResult = result;

                    RaiseResult(result);
                });
            }

            private void RaiseResult(ResolverResult result)
            {
                if (result.Addresses != null)
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
                    var orderedAddresses = result.Addresses.OrderBy(a => a.EndPoint.Host).ToList();
                    Listener(ResolverResult.ForResult(orderedAddresses, serviceConfig, Status.DefaultSuccess));
                }
                else
                {
                    Listener(ResolverResult.ForFailure(result.Status));
                }
            }
        }
    }
}
