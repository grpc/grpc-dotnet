using Grpc.Net.Client.LoadBalancing.Policies;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.Policies
{
    public sealed class RoundRobinPolicyTests
    {
        [Fact]
        public async Task ForEmptyResolutionPassed_UseRoundRobinPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new RoundRobinPolicy();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                var _ = await policy.CreateSubChannelsAsync(new List<GrpcNameResolutionResult>(), false);
            });
        }

        [Fact]
        public async Task ForBalancersResolutionOnly_UseRoundRobinPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new RoundRobinPolicy();
            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.6.120", 80)
                {
                    IsLoadBalancer = true
                },
                new GrpcNameResolutionResult("10.1.6.121", 80)
                {
                    IsLoadBalancer = true
                }
            };

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                var _ = await policy.CreateSubChannelsAsync(resolutionResults, false); // load balancers are ignored
            });
        }

        [Fact]
        public async Task ForResolutionResults_UseRoundRobinPolicy_CreateAmmountSubChannels()
        {
            // Arrange
            var policy = new RoundRobinPolicy();
            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.5.211", 80)
                {
                    IsLoadBalancer = false
                },
                new GrpcNameResolutionResult("10.1.5.212", 80)
                {
                    IsLoadBalancer = false
                },
                new GrpcNameResolutionResult("10.1.5.213", 80)
                {
                    IsLoadBalancer = false
                },
                new GrpcNameResolutionResult("10.1.5.214", 80)
                {
                    IsLoadBalancer = false
                }
            };

            // Act
            var subChannels = await policy.CreateSubChannelsAsync(resolutionResults, false);

            // Assert
            Assert.Equal(4, subChannels.Count);
            Assert.All(subChannels, subChannel => Assert.Equal("http", subChannel.Address.Scheme));
            Assert.All(subChannels, subChannel => Assert.Equal(80, subChannel.Address.Port));
            Assert.All(subChannels, subChannel => Assert.StartsWith("10.1.5.", subChannel.Address.Host));
        }

        [Fact]
        public async Task ForResolutionResultWithBalancers_UseRoundRobinPolicy_IgnoreBalancersCreateSubchannels()
        {
            // Arrange
            var policy = new RoundRobinPolicy();
            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.5.211", 8443)
                {
                    IsLoadBalancer = false,
                },
                new GrpcNameResolutionResult("10.1.5.212", 8443)
                {
                    IsLoadBalancer = false
                },
                new GrpcNameResolutionResult("10.1.6.120", 80)
                {
                    IsLoadBalancer = true
                },
                new GrpcNameResolutionResult("10.1.6.121", 80)
                {
                    IsLoadBalancer = true
                },
                new GrpcNameResolutionResult("10.1.5.214", 8443)
                {
                    IsLoadBalancer = false
                }
            };

            // Act
            var subChannels = await policy.CreateSubChannelsAsync(resolutionResults, true);

            // Assert
            Assert.Equal(3, subChannels.Count); // load balancers are ignored
            Assert.All(subChannels, subChannel => Assert.Equal("https", subChannel.Address.Scheme));
            Assert.All(subChannels, subChannel => Assert.Equal(8443, subChannel.Address.Port));
            Assert.All(subChannels, subChannel => Assert.StartsWith("10.1.5.", subChannel.Address.Host));
        }

        [Fact]
        public void ForGrpcSubChannels_UseRoundRobinPolicySelectChannels_SelectChannelsInRoundRobin()
        {
            // Arrange
            var policy = new RoundRobinPolicy();
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
    }
}
