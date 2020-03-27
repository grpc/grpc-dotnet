using DnsClient;
using DnsClient.Protocol;
using Grpc.Net.Client.LoadBalancing.ResolverPlugins;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.ResolverPlugins
{
    public sealed class DnsClientResolverPluginTests
    {
        [Fact]
        public async Task ForTargetWithNonDnsScheme_UseDnsClientResolverPlugin_ThrowArgumentException()
        {
            // Arrange
            var resolverPlugin = new DnsClientResolverPlugin();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await resolverPlugin.StartNameResolutionAsync(new Uri("http://sample.host.com"));
            });
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await resolverPlugin.StartNameResolutionAsync(new Uri("https://sample.host.com"));
            });
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await resolverPlugin.StartNameResolutionAsync(new Uri("unknown://sample.host.com"));
            });
        }


        [Fact]
        public async Task ForTargetAndEmptyDnsResults_UseDnsClientResolverPlugin_ReturnNoFinidings()
        {
            // Arrange
            var serviceHostName = "my-service";
            var txtDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var srvBalancersDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var srvServersDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var dnsClientMock = new Mock<IDnsQuery>(MockBehavior.Strict);

            txtDnsQueryResponse.Setup(x => x.Answers).Returns(new List<TxtRecord>().AsReadOnly());
            srvBalancersDnsQueryResponse.Setup(x => x.Answers).Returns(new List<SrvRecord>().AsReadOnly());
            srvServersDnsQueryResponse.Setup(x => x.Answers).Returns(new List<SrvRecord>().AsReadOnly());

            dnsClientMock.Setup(x => x.QueryAsync(It.IsAny<string>(), QueryType.TXT, QueryClass.IN, default))
                .Returns(Task.FromResult(txtDnsQueryResponse.Object));
            dnsClientMock.Setup(x => x.QueryAsync(It.IsAny<string>(), QueryType.SRV, QueryClass.IN, default))
                .Returns(Task.FromResult(srvBalancersDnsQueryResponse.Object));
            dnsClientMock.Setup(x => x.QueryAsync(It.IsAny<string>(), QueryType.SRV, QueryClass.IN, default))
                .Returns(Task.FromResult(srvServersDnsQueryResponse.Object));

            var resolverPlugin = new DnsClientResolverPlugin();
            resolverPlugin.OverrideDnsClient = dnsClientMock.Object;

            // Act
            var resolutionResult = await resolverPlugin.StartNameResolutionAsync(new Uri($"dns://{serviceHostName}"));

            // Assert
            Assert.Empty(resolutionResult);
        }

        [Fact]
        public async Task ForTargetAndBalancerSrvRecords_UseDnsClientResolverPlugin_ReturnBalancers()
        {
            // Arrange
            var serviceHostName = "my-service";
            var txtDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var srvBalancersDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var srvServersDnsQueryResponse = new Mock<IDnsQueryResponse>(MockBehavior.Strict);
            var dnsClientMock = new Mock<IDnsQuery>(MockBehavior.Strict);

            txtDnsQueryResponse.Setup(x => x.Answers).Returns(new List<TxtRecord>().AsReadOnly());
            srvBalancersDnsQueryResponse.Setup(x => x.Answers).Returns(new List<SrvRecord>(GetBalancersSrvRecords(serviceHostName)).AsReadOnly());
            srvServersDnsQueryResponse.Setup(x => x.Answers).Returns(new List<SrvRecord>(GetServersSrvRecords(serviceHostName)).AsReadOnly());

            dnsClientMock.Setup(x => x.QueryAsync($"_grpc_config.{serviceHostName}", QueryType.TXT, QueryClass.IN, default))
                .Returns(Task.FromResult(txtDnsQueryResponse.Object));
            dnsClientMock.Setup(x => x.QueryAsync($"_grpclb._tcp.{serviceHostName}", QueryType.SRV, QueryClass.IN, default))
                .Returns(Task.FromResult(srvBalancersDnsQueryResponse.Object));
            dnsClientMock.Setup(x => x.QueryAsync($"_grpc._tcp.{serviceHostName}", QueryType.SRV, QueryClass.IN, default))
                .Returns(Task.FromResult(srvServersDnsQueryResponse.Object));

            var resolverPlugin = new DnsClientResolverPlugin();
            resolverPlugin.OverrideDnsClient = dnsClientMock.Object;

            // Act
            var resolutionResult = await resolverPlugin.StartNameResolutionAsync(new Uri($"dns://{serviceHostName}"));

            // Assert
            Assert.Equal(5, resolutionResult.Count);
            Assert.Equal(2, resolutionResult.Where(x => x.IsLoadBalancer).Count());
            Assert.All(resolutionResult.Where(x => x.IsLoadBalancer), x => Assert.Equal(80, x.Port));
            Assert.All(resolutionResult.Where(x => x.IsLoadBalancer), x => Assert.StartsWith("10-1-6-", x.Host));
            Assert.Equal(3, resolutionResult.Where(x => !x.IsLoadBalancer).Count());
            Assert.All(resolutionResult.Where(x => !x.IsLoadBalancer), x => Assert.Equal(443, x.Port));
            Assert.All(resolutionResult.Where(x => !x.IsLoadBalancer), x => Assert.StartsWith("10-1-5-", x.Host));
        }

        private List<SrvRecord> GetBalancersSrvRecords(string serviceHostName)
        {
            return new List<SrvRecord>()
            {
                new SrvRecord(new ResourceRecordInfo($"_grpclb._tcp.{serviceHostName}", ResourceRecordType.SRV, QueryClass.IN, 30, 50), 0, 0, 80, DnsString.Parse($"10-1-6-120.{serviceHostName}")),
                new SrvRecord(new ResourceRecordInfo($"_grpclb._tcp.{serviceHostName}", ResourceRecordType.SRV, QueryClass.IN, 30, 50), 0, 0, 80, DnsString.Parse($"10-1-6-121.{serviceHostName}"))
            };
        }

        private List<SrvRecord> GetServersSrvRecords(string serviceHostName)
        {
            return new List<SrvRecord>()
            {
                new SrvRecord(new ResourceRecordInfo($"_grpc._tcp.{serviceHostName}", ResourceRecordType.SRV, QueryClass.IN, 30, 50), 0, 0, 443, DnsString.Parse($"10-1-5-211.{serviceHostName}")),
                new SrvRecord(new ResourceRecordInfo($"_grpc._tcp.{serviceHostName}", ResourceRecordType.SRV, QueryClass.IN, 30, 50), 0, 0, 443, DnsString.Parse($"10-1-5-212.{serviceHostName}")),
                new SrvRecord(new ResourceRecordInfo($"_grpc._tcp.{serviceHostName}", ResourceRecordType.SRV, QueryClass.IN, 30, 50), 0, 0, 443, DnsString.Parse($"10-1-5-213.{serviceHostName}"))
            };
        }
    }
}
