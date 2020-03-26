#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grpc.Net.Client
{
    public interface IGrpcResolverPlugin
    {
        ILoggerFactory LoggerFactory { set; }
        Task<List<GrpcNameResolutionResult>> StartNameResolutionAsync(Uri target);
    }

    public interface IGrpcLoadBalancingPolicy
    {
        ILoggerFactory LoggerFactory { set; }
        Task<List<GrpcSubChannel>> CreateSubChannelsAsync(List<GrpcNameResolutionResult> resolutionResult, bool isSecureConnection);
        GrpcSubChannel GetNextSubChannel(List<GrpcSubChannel> subChannels);
    }

    public sealed class GrpcNameResolutionResult
    {
        public string Host { get; set; }
        public int? Port { get; set; } = null;
        public bool IsLoadBalancer { get; set; } = false;
        public int Priority { get; set; } = 0;
        public int Weight { get; set; } = 0;

        public GrpcNameResolutionResult(string host, int? port = null)
        {
            Host = host;
            Port = port;
        }
    }

    public sealed class GrpcSubChannel
    {
        internal Uri Address { get; }

        public GrpcSubChannel(Uri address)
        {
            Address = address;
        }
    }

    /// <summary>
    /// Assume name was already resolved or pass through to HttpClient to handle
    /// </summary>
    internal sealed class NoneResolverPlugin : IGrpcResolverPlugin
    {
        private ILogger _logger = NullLogger.Instance;
        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<NoneResolverPlugin>();
        }

        public Task<List<GrpcNameResolutionResult>> StartNameResolutionAsync(Uri target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (target.Scheme.Equals("dns", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("dns:// scheme require non-default name resolver in channelOptions.ResolverPlugin");
            }
            _logger.LogDebug($"Name resolver using defined target as name resolution");
            return Task.FromResult(new List<GrpcNameResolutionResult>()
            {
               new GrpcNameResolutionResult(target.Host, target.Port)
            });
        }
    }

    internal sealed class PickFirstPolicy : IGrpcLoadBalancingPolicy
    {
        private ILogger _logger = NullLogger.Instance;
        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<PickFirstPolicy>();
        }

        public Task<List<GrpcSubChannel>> CreateSubChannelsAsync(List<GrpcNameResolutionResult> resolutionResult, bool isSecureConnection)
        {
            if (resolutionResult == null)
            {
                throw new ArgumentNullException(nameof(resolutionResult));
            }
            resolutionResult = resolutionResult.Where(x => !x.IsLoadBalancer).ToList();
            if (resolutionResult.Count == 0)
            {
                throw new ArgumentException($"{nameof(resolutionResult)} must contain at least one non-blancer address");
            }
            _logger.LogDebug($"Start first_pick policy");
            var uriBuilder = new UriBuilder();
            uriBuilder.Host = resolutionResult[0].Host;
            uriBuilder.Port = resolutionResult[0].Port ?? (isSecureConnection ? 443 : 80);
            uriBuilder.Scheme = isSecureConnection ? "https" : "http";
            var uri = uriBuilder.Uri;
            var result = new List<GrpcSubChannel> {
                new GrpcSubChannel(uri)
            };
            _logger.LogDebug($"Found a server {uri}");
            _logger.LogDebug($"SubChannels list created");
            return Task.FromResult(result);
        }

        public GrpcSubChannel GetNextSubChannel(List<GrpcSubChannel> subChannels)
        {
            return subChannels[0];
        }
    }
}
