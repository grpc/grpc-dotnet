#pragma warning disable CA1812 // Classes in this file are used for deserialization
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins.GrpcServiceConfig
{
    // based on: https://github.com/grpc/proposal/blob/master/A2-service-configs-in-dns.md
    internal sealed class GrpcConfig
    {
        public ServiceConfig ServiceConfig { get; set; } = new ServiceConfig();
    }

    //based on: https://github.com/grpc/grpc-proto/blob/master/grpc/service_config/service_config.proto
    internal sealed class ServiceConfig
    {
        // This field is deprecated but currently widely used
        public string LoadBalancingPolicy { get; set; } = string.Empty;
        public List<LoadBalancingConfig> LoadBalancingConfig { get; set; } = new List<LoadBalancingConfig>();
        public string[] GetLoadBalancingPolicies()
        {
            if (LoadBalancingConfig.Count != 0)
            {
                return LoadBalancingConfig.Select(x => x.GetPolicyName()).ToArray();
            }
            if (LoadBalancingPolicy != string.Empty)
            {
                return new string[] { LoadBalancingPolicy };
            }
            else
            {
                throw new InvalidOperationException("Invalid ServiceConfig, load balancing policy must be specified");
            }
        }
    }

    internal sealed class LoadBalancingConfig
    {
        public PickFirstConfig? PickFirst { get; set; }
        public RoundRobinConfig? RoundRobin { get; set; }
        public GrpcLbConfig? Grpclb { get; set; }

        //XDS policy config should be added here in the future

        public string GetPolicyName()
        {
            // according to proto file only one configuration can be specified 
            return Grpclb?.ToString() ?? RoundRobin?.ToString() ?? PickFirst?.ToString()
                ?? throw new InvalidOperationException("Load balancing config without policy defined");
        }
    }

    internal sealed class PickFirstConfig
    {
        //This should be left empty, see service_config.proto file

        public override string ToString()
        {
            return "pick_first";
        }
    }

    internal sealed class RoundRobinConfig
    {
        //This should be left empty, see service_config.proto file

        public override string ToString()
        {
            return "round_robin";
        }
    }

    internal sealed class GrpcLbConfig
    {
        public List<LoadBalancingConfig>? ChildPolicy { get; set; }

        public string? ServiceName { get; set; }

        public override string ToString()
        {
            return "grpclb";
        }
    }
}
