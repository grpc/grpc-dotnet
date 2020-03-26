#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using DnsClient;
using DnsClient.Protocol;
using Grpc.Net.Client.LoadBalancing.ResolverPlugins.GrpcServiceConfig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins
{
    public sealed class DnsClientResolverPlugin : IGrpcResolverPlugin
    {
        private DnsClientResolverPluginOptions _options;
        private ILogger _logger = NullLogger.Instance;

        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<DnsClientResolverPlugin>();
        }

        public DnsClientResolverPlugin() : this(new DnsClientResolverPluginOptions())
        {
        }

        public DnsClientResolverPlugin(DnsClientResolverPluginOptions options)
        {
            _options = options;
        }

        public async Task<List<GrpcNameResolutionResult>> StartNameResolutionAsync(Uri target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (!target.Scheme.Equals("dns", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"{nameof(DnsClientResolverPlugin)} require dns:// scheme to set as target address");
            }
            var host = target.Host;
            var dnsClient = GetDnsClient();
            if (!_options.DisableTxtServiceConfig)
            {
                var serviceConfigDnsQuery = $"_grpc_config.{host}";
                _logger.LogDebug($"Start TXT lookup for {serviceConfigDnsQuery}");
                var txtRecords = (await dnsClient.QueryAsync(serviceConfigDnsQuery, QueryType.TXT).ConfigureAwait(false)).Answers.OfType<TxtRecord>().ToArray();
                _logger.LogDebug($"Number of TXT records found: {txtRecords.Length}");
                var grpcConfigs = txtRecords.SelectMany(x => x.Text).Where(IsGrpcConfigTxtRecord).ToArray();
                _logger.LogDebug($"Number of grpc_configs found: {grpcConfigs.Length}");
                if(grpcConfigs.Length != 0 && TryParseGrpcConfig(grpcConfigs[0], out var serviceConfigs))
                {
                    _logger.LogDebug($"First grpc_config is selected " + grpcConfigs[0]);
                    _logger.LogDebug($"Parsing JSON grpc_config into service config success");
                    var serviceConfig = serviceConfigs[0];
                    _logger.LogDebug($"Service config defines policies: {string.Join(',', serviceConfig.GetLoadBalancingPolicies())}");
                }
                else
                {
                    _logger.LogDebug($"Parsing JSON grpc_config into service config failed, loading service config is skipped");
                }
            }
            var balancingDnsQuery = $"_grpclb._tcp.{host}";
            var serversDnsQuery = $"_grpc._tcp.{host}";
            _logger.LogDebug($"Start SRV lookup for {balancingDnsQuery} and {serversDnsQuery}");
            var balancingDnsQueryTask = dnsClient.QueryAsync(balancingDnsQuery, QueryType.SRV);
            var serversDnsQueryTask = dnsClient.QueryAsync(serversDnsQuery, QueryType.SRV);
            await Task.WhenAll(balancingDnsQueryTask, serversDnsQueryTask).ConfigureAwait(false);
            var results = balancingDnsQueryTask.Result.Answers.OfType<SrvRecord>().Select(x => ParseSrvRecord(x, true))
                .Union(serversDnsQueryTask.Result.Answers.OfType<SrvRecord>().Select(x => ParseSrvRecord(x, false))).ToList();
            if (results.Count == 0)
            {
                _logger.LogDebug($"Not found any SRV records");
                return new List<GrpcNameResolutionResult>();
            }
            return results;
        }

        private LookupClient GetDnsClient()
        {
            if (_options.NameServers.Length == 0)
            {
                return new LookupClient();
            }
            else
            {
                _logger.LogDebug($"Override DNS name servers using options");
                return new LookupClient(_options.NameServers);
            }
        }

        private static bool IsGrpcConfigTxtRecord(string txtRecordText)
        {
            return txtRecordText.StartsWith("grpc_config=", StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool TryParseGrpcConfig(string txtRecordText, out ServiceConfig[] serviceConfigs)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var txtRecordValue = txtRecordText.Substring(12); // remove txt key -> grpc_config= 
                serviceConfigs = JsonSerializer.Deserialize<GrpcConfig[]>(txtRecordValue, options)
                    .Select(x => x.ServiceConfig).ToArray();
                return true;
            }
            catch (Exception)
            {
                serviceConfigs = Array.Empty<ServiceConfig>();
                return false;
            }
        }
        
        private GrpcNameResolutionResult ParseSrvRecord(SrvRecord srvRecord, bool isLoadBalancer)
        {
            _logger.LogDebug($"Found a SRV record {srvRecord.ToString()}");
            return new GrpcNameResolutionResult(srvRecord.Target)
            {
                Port = srvRecord.Port,
                IsLoadBalancer = isLoadBalancer,
                Priority = srvRecord.Priority,
                Weight = srvRecord.Weight
            };
        }
    }
}
