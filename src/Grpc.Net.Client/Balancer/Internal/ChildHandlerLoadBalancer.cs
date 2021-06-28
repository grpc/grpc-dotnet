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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Shared;

namespace Grpc.Net.Client.Balancer.Internal
{
    /// <summary>
    /// The load balancer is responsible for creating the real load balancer, and changing
    /// it if the resolver returns a service config with a new policy name.
    /// <para>
    /// This load balancer has a reference to both a current and a pending load balancer.
    /// 
    /// If there is an update that prompts the load balancer to change then:
    /// 1. New load balancer is created and set to pending. It will begin connecting.
    /// 2. Current load balancer will continue to serve new gRPC calls.
    /// 3. Once pending load balancer has a READY state then it is prompted to current.
    /// 4. Old current load balancer is disposed.
    /// 
    /// This is designed so that there is a smooth transistion when the load balancer changes.
    /// </para>
    /// </summary>
    internal sealed class ChildHandlerLoadBalancer : LoadBalancer
    {
        private static readonly PickFirstConfig DefaultConfig = new PickFirstConfig();
        private static readonly BalancerAttributesKey<LoadBalancer> LoadBalancerKey = new BalancerAttributesKey<LoadBalancer>("LoadBalancer");

        private readonly IChannelControlHelper _controller;
        private readonly ServiceConfig? _initialServiceConfig;
        private readonly ConnectionManager _connectionManager;
        private readonly object _lock = new object();

        internal (LoadBalancer LoadBalancer, string Name)? _current;
        internal (LoadBalancer LoadBalancer, string Name)? _pending;

        public ChildHandlerLoadBalancer(IChannelControlHelper controller, ServiceConfig? initialServiceConfig, ConnectionManager connectionManager)
        {
            _controller = controller;
            _initialServiceConfig = initialServiceConfig;
            _connectionManager = connectionManager;
        }

        public override void UpdateChannelState(ChannelState state)
        {
            (LoadBalancer LoadBalancer, string Name) childToUpdate;

            // Resolver returned a service config.
            // With load balancing configs.
            if (state.LoadBalancingConfig != null)
            {
                if (TryGetFactory(state.LoadBalancingConfig.PolicyName, _connectionManager.LoadBalancerFactories, out var factory))
                {
                    if (_current == null)
                    {
                        _current = CreateLoadBalancer(factory, state.LoadBalancingConfig);
                    }
                    else if (_current?.Name != state.LoadBalancingConfig.PolicyName)
                    {
                        // Load balancing config is not what we're currently using.

                        // Already have a pending load balancer. Dispose it and start over.
                        _pending?.LoadBalancer.Dispose();
                        _pending = CreateLoadBalancer(factory, state.LoadBalancingConfig);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Couldn't resolve load balancing policy {state.LoadBalancingConfig.PolicyName} to a factory");
                }
            }

            lock (_lock)
            {
                if (_pending == null)
                {
                    if (_current == null)
                    {
                        if (_initialServiceConfig != null && _initialServiceConfig.LoadBalancingConfigs.Count > 0)
                        {
                            if (TryGetValidServiceConfigFactory(_initialServiceConfig.LoadBalancingConfigs, _connectionManager.LoadBalancerFactories, out var lbConfig, out var factory))
                            {
                                _current = CreateLoadBalancer(factory, lbConfig);
                            }
                            else
                            {
                                throw new InvalidOperationException($"No load balancers configured for {string.Join(", ", _initialServiceConfig.LoadBalancingConfigs.Select(c => $"'{c.PolicyName}'"))}.");
                            }
                        }
                        else if (TryGetFactory(LoadBalancingConfig.PickFirstPolicyName, _connectionManager.LoadBalancerFactories, out var defaultFactory))
                        {
                            // Default to pick_first when no configuration supplied.
                            _current = CreateLoadBalancer(defaultFactory, DefaultConfig);
                        }
                        else
                        {
                            throw new InvalidOperationException("Unable to create default load balancer.");
                        }
                    }

                    childToUpdate = _current.Value;
                }
                else
                {
                    childToUpdate = _pending.Value;
                }
            }

            childToUpdate.LoadBalancer.UpdateChannelState(state);
        }

        private (LoadBalancer, string) CreateLoadBalancer(LoadBalancerFactory factory, LoadBalancingConfig lbConfig)
        {
            var configuration = (IDictionary<string, object>)lbConfig.Inner[lbConfig.PolicyName];

            var controller = new ChildHandlerController(this);
            var loadBalancer = factory.Create(new LoadBalancerOptions(controller, _connectionManager.LoggerFactory, configuration));
            var result = (loadBalancer, lbConfig.PolicyName);
            controller.Child = result;

            return result;
        }

        /// <summary>
        /// Iterate through the load balancing configs and find the first config that has a factory.
        /// That means it is supported by the client.
        /// https://github.com/grpc/proposal/blob/master/A24-lb-policy-config.md
        /// </summary>
        public static bool TryGetValidServiceConfigFactory(
            IList<LoadBalancingConfig> loadBalancingConfigs,
            LoadBalancerFactory[] factories,
            [NotNullWhen(true)] out LoadBalancingConfig? loadBalancingConfig,
            [NotNullWhen(true)] out LoadBalancerFactory? factory)
        {
            if (loadBalancingConfigs.Count > 0)
            {
                for (var i = 0; i < loadBalancingConfigs.Count; i++)
                {
                    var lbConfig = loadBalancingConfigs[i];
                    var policyName = lbConfig.PolicyName;

                    if (TryGetFactory(policyName, factories, out factory))
                    {
                        loadBalancingConfig = lbConfig;
                        return true;
                    }
                }
            }

            loadBalancingConfig = null;
            factory = null;
            return false;
        }

        public override void RequestConnection()
        {
            (_pending ?? _current)?.LoadBalancer.RequestConnection();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _current?.LoadBalancer.Dispose();
            _pending?.LoadBalancer.Dispose();
        }

        public static bool TryGetFactory(string name, LoadBalancerFactory[] factories, [NotNullWhen(true)]out LoadBalancerFactory? factory)
        {
            for (var i = 0; i < factories.Length; i++)
            {
                if (string.Equals(factories[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    factory = factories[i];
                    return true;
                }
            }

            factory = null;
            return false;
        }

        private sealed class ChildHandlerController : IChannelControlHelper
        {
            private readonly ChildHandlerLoadBalancer _parent;

            public (LoadBalancer LoadBalancer, string Name)? Child { get; set; }

            public ChildHandlerController(ChildHandlerLoadBalancer parent)
            {
                _parent = parent;
            }

            public Subchannel CreateSubchannel(SubchannelOptions options)
            {
                var subchannel = _parent._controller.CreateSubchannel(options);
                subchannel.Attributes.Set(LoadBalancerKey, Child!.Value.LoadBalancer);

                return subchannel;
            }

            public void RefreshResolver()
            {
                (LoadBalancer LoadBalancer, string Name)? latest;
                lock (_parent._lock)
                {
                    latest = _parent._pending ?? _parent._current;
                }

                if (Child == latest)
                {
                    _parent._controller.RefreshResolver();
                }
            }

            public void UpdateState(BalancerState state)
            {
                lock (_parent._lock)
                {
                    if (Child == _parent._pending)
                    {
                        if (state.ConnectivityState != ConnectivityState.Ready)
                        {
                            // Ignore pending load balancer state updates until ready.
                            return;
                        }

                        // Pending has sent a ready status. Replace current with pending.
                        _parent._current?.LoadBalancer.Dispose();
                        _parent._current = _parent._pending;
                        _parent._pending = null;
                    }
                    else if (Child != _parent._current)
                    {
                        // Update is from neither current or pending. Ignore.
                        return;
                    }
                }

                _parent._controller.UpdateState(state);
            }
        }
    }
}
#endif
