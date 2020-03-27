using System;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.ResolverPlugins
{
    public sealed class NoneResolverPluginTests
    {
        [Fact]
        public async Task ForTarget_UseNoneResolverPlugin_ReturnResolutionResultWithTheSameValue()
        {
            // Arrange
            var resolverPlugin = new NoneResolverPlugin();

            // Act
            var resolutionResult = await resolverPlugin.StartNameResolutionAsync(new Uri("https://sample.host.com"));

            // Assert
            Assert.Single(resolutionResult);
            Assert.Equal("sample.host.com", resolutionResult[0].Host);
            Assert.Equal(443, resolutionResult[0].Port);
            Assert.False(resolutionResult[0].IsLoadBalancer);
        }

        [Fact]
        public async Task ForTargetWithDnsScheme_UseNoneResolverPlugin_ThrowArgumentException()
        {
            // Arrange
            var resolverPlugin = new NoneResolverPlugin();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await resolverPlugin.StartNameResolutionAsync(new Uri("dns://sample.host.com"));
            });
        }
    }
}
