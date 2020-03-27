using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.Policies
{
    public sealed class PickFirstPolicyTests
    {
        [Fact]
        public async Task ForEmptyResolutionPassed_UsePickFirstPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new PickFirstPolicy();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                var _ = await policy.CreateSubChannelsAsync(new List<GrpcNameResolutionResult>(), false);
            });
        }

        [Fact]
        public async Task ForBalancersResolutionOnly_UsePickFirstPolicy_ThrowArgumentException()
        {
            // Arrange
            var policy = new PickFirstPolicy();
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
        public async Task ForResolutionResults_UsePickFirstPolicy_CreateAmmountSubChannels()
        {
            // Arrange
            var policy = new PickFirstPolicy();
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
            Assert.Single(subChannels);
            Assert.Equal("http", subChannels[0].Address.Scheme);
            Assert.Equal(80, subChannels[0].Address.Port);
            Assert.StartsWith("10.1.5.211", subChannels[0].Address.Host);
        }

        [Fact]
        public async Task ForResolutionResultWithBalancers_UsePickFirstPolicy_IgnoreBalancersCreateSubchannels()
        {
            // Arrange
            var policy = new PickFirstPolicy();
            var resolutionResults = new List<GrpcNameResolutionResult>()
            {
                new GrpcNameResolutionResult("10.1.6.120", 80)
                {
                    IsLoadBalancer = true
                },
                new GrpcNameResolutionResult("10.1.5.212", 8443)
                {
                    IsLoadBalancer = false
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
            Assert.Single(subChannels); // load balancers are ignored
            Assert.Equal("https", subChannels[0].Address.Scheme);
            Assert.Equal(8443, subChannels[0].Address.Port);
            Assert.StartsWith("10.1.5.212", subChannels[0].Address.Host);
        }

        [Fact]
        public void ForGrpcSubChannels_UsePickFirstPolicySelectChannels_SelectFirstChannel()
        {
            // Arrange
            var policy = new PickFirstPolicy();
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
                Assert.Equal(subChannels[0].Address.Host, subChannel.Address.Host);
                Assert.Equal(subChannels[0].Address.Port, subChannel.Address.Port);
                Assert.Equal(subChannels[0].Address.Scheme, subChannel.Address.Scheme);
            }
        }
    }
}
