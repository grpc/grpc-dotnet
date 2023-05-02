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

using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;

namespace Frontend.Balancer
{
    public class ConfigurableResolverFactory : ResolverFactory
    {
        private static readonly BalancerAttributesKey<string> HostOverrideKey = new BalancerAttributesKey<string>("HostOverride");

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
            return new ConfigurableResolver(_innerResolverFactory.Create(options), _balancerConfiguration);
        }

        private class ConfigurableResolver : Resolver
        {
            private readonly Resolver _innerResolver;
            private readonly BalancerConfiguration _balancerConfiguration;

            private ResolverResult? _lastResult;
            private Action<ResolverResult>? _listener;

            public ConfigurableResolver(Resolver innerResolver, BalancerConfiguration balancerConfiguration)
            {
                _innerResolver = innerResolver;
                _balancerConfiguration = balancerConfiguration;
                _balancerConfiguration.Updated += OnConfigurationUpdated;
            }

            public override void Start(Action<ResolverResult> listener)
            {
                _listener = listener;
                _innerResolver.Start(result =>
                {
                    _lastResult = result;

                    RaiseResult(result);
                });
            }

            public override void Refresh()
            {
                _innerResolver.Refresh();
            }

            private void OnConfigurationUpdated(object? sender, EventArgs e)
            {
                // Can't just call RefreshAsync and get new results because of rate limiting.
                if (_lastResult != null)
                {
                    RaiseResult(_lastResult);
                }
            }

            private void RaiseResult(ResolverResult result)
            {
                if (_listener != null)
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
                        // Remove host override from addresses so the destination IP address is available.
                        // The sample does this because the server returns the IP address to the client.
                        // This makes it clear that gRPC calls are balanced between pods.
                        foreach (var address in orderedAddresses)
                        {
                            ((IDictionary<string, object?>)address.Attributes).Remove(HostOverrideKey.Key);
                        }
                        _listener(ResolverResult.ForResult(orderedAddresses, serviceConfig, Status.DefaultSuccess));
                    }
                    else
                    {
                        _listener(ResolverResult.ForFailure(result.Status));
                    }
                }
            }
        }
    }
}
