using Google.Protobuf;
using Grpc.Core;
using Grpc.Lb.V1;
using Grpc.Net.Client.LoadBalancing.Policies;
using Grpc.Net.Client.LoadBalancing.Policies.Abstraction;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.Policies
{
    public sealed class GrpclbPolicyTests
    {
        [Fact]
        public async Task ForEmptyResolutionPassed_UseGrpclbPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new GrpclbPolicy();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                var _ = await policy.CreateSubChannelsAsync(new List<GrpcNameResolutionResult>(), false);
            });
        }

        [Fact]
        public async Task ForServersResolutionOnly_UseGrpclbPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new GrpclbPolicy();
            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.5.211", 80)
                {
                    IsLoadBalancer = false
                },
                new GrpcNameResolutionResult("10.1.5.212", 80)
                {
                    IsLoadBalancer = false
                }
            };

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                var _ = await policy.CreateSubChannelsAsync(resolutionResults, false); // non-balancers are ignored
            });
        }

        [Fact]
        public async Task ForResolutionResultWithBalancers_UseGrpclbPolicy_CreateSubchannelsForFoundServers()
        {
            // Arrange
            var balancerClientMock = new Mock<ILoadBalancerClient>(MockBehavior.Strict);
            var balancerStreamMock = new Mock<IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse>>(MockBehavior.Strict);
            var requestStreamMock = new Mock<IClientStreamWriter<LoadBalanceRequest>>(MockBehavior.Loose);
            var responseStreamMock = new Mock<IAsyncStreamReader<LoadBalanceResponse>>(MockBehavior.Loose);

            balancerClientMock.Setup(x => x.Dispose()).Verifiable();
            balancerClientMock.Setup(x => x.BalanceLoad(null, null, It.IsAny<CancellationToken>()))
                .Returns(balancerStreamMock.Object);

            balancerStreamMock.Setup(x => x.RequestStream).Returns(requestStreamMock.Object);
            balancerStreamMock.Setup(x => x.ResponseStream).Returns(responseStreamMock.Object);

            var responseCounter = 0;
            responseStreamMock.Setup(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(responseCounter++ == 0));
            responseStreamMock.Setup(x => x.Current).Returns(GetSampleLoadBalanceResponse());

            var policy = new GrpclbPolicy();
            policy.OverrideLoadBalancerClient = balancerClientMock.Object;

            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.6.120", 80) { IsLoadBalancer = true }
            };

            // Act
            var subChannels = await policy.CreateSubChannelsAsync(resolutionResults, false);

            // Assert
            Assert.Equal(3, subChannels.Count); // subChannels are created per results from GetSampleLoadBalanceResponse
            Assert.All(subChannels, subChannel => Assert.Equal("http", subChannel.Address.Scheme));
            Assert.All(subChannels, subChannel => Assert.Equal(80, subChannel.Address.Port));
            Assert.All(subChannels, subChannel => Assert.StartsWith("10.1.5.", subChannel.Address.Host));
            balancerClientMock.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public void ForGrpcSubChannels_UseGrpclbPolicySelectChannels_SelectChannelsInRoundRobin()
        {
            // Arrange
            var policy = new GrpclbPolicy();
            var subChannels = new List<GrpcSubChannel>()
            {
                new GrpcSubChannel(new UriBuilder("http://10.1.5.210:80").Uri),
                new GrpcSubChannel(new UriBuilder("http://10.1.5.212:80").Uri),
                new GrpcSubChannel(new UriBuilder("http://10.1.5.211:80").Uri),
                new GrpcSubChannel(new UriBuilder("http://10.1.5.213:80").Uri)
            };
            // Act
            // Assert
            for (int i = 0; i < 30; i++)
            {
                var subChannel = policy.GetNextSubChannel(subChannels);
                Assert.Equal(subChannels[i % subChannels.Count].Address.Host, subChannel.Address.Host);
                Assert.Equal(subChannels[i % subChannels.Count].Address.Port, subChannel.Address.Port);
                Assert.Equal(subChannels[i % subChannels.Count].Address.Scheme, subChannel.Address.Scheme);
            }
        }

        private static LoadBalanceResponse GetSampleLoadBalanceResponse()
        {
            var responseServerList = new ServerList();
            responseServerList.Servers.Add(new Server()
            {
                IpAddress = ByteString.CopyFrom(IPAddress.Parse("10.1.5.211").GetAddressBytes()),
                Port = 80
            });
            responseServerList.Servers.Add(new Server()
            {
                IpAddress = ByteString.CopyFrom(IPAddress.Parse("10.1.5.212").GetAddressBytes()),
                Port = 80
            });
            responseServerList.Servers.Add(new Server()
            {
                IpAddress = ByteString.CopyFrom(IPAddress.Parse("10.1.5.213").GetAddressBytes()),
                Port = 80
            });
            return new LoadBalanceResponse() { ServerList = responseServerList };
        }
    }
}
