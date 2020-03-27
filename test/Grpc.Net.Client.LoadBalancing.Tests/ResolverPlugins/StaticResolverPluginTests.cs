using Grpc.Net.Client.LoadBalancing.ResolverPlugins;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Grpc.Net.Client.LoadBalancing.Tests.ResolverPlugins
{
    public sealed class StaticResolverPluginTests
    {
        [Fact]
        public async Task ForStaticResolutionFunction_UseStaticResolverPlugin_ReturnPredefinedValues()
        {
            // Arrange
            Func<Uri, List<GrpcNameResolutionResult>> resolveFunction = (uri) =>
            {
                return new List<GrpcNameResolutionResult>()
                {
                    new GrpcNameResolutionResult("10.1.5.212", 8080),
                    new GrpcNameResolutionResult("10.1.5.213", 8080)
                };
            };
            var resolverPlugin = new StaticResolverPlugin(resolveFunction);

            // Act
            var resolutionResult = await resolverPlugin.StartNameResolutionAsync(new Uri("https://sample.host.com"));

            // Assert
            Assert.Equal(2, resolutionResult.Count);
            Assert.Equal("10.1.5.212", resolutionResult[0].Host);
            Assert.Equal("10.1.5.213", resolutionResult[1].Host);
            Assert.Equal(8080, resolutionResult[0].Port);
            Assert.Equal(8080, resolutionResult[1].Port);
        }
    }
}
