using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class CustomClientFactoryTests
    {
        private static IServiceProvider SharedRegistraction(bool registerCustomProvider)
        {
            var services = new ServiceCollection();
            services.AddGrpcClient<DefaultLookingClient>(o =>
            {
                o.Address = new Uri("https://nowhere.com/");
            });
            // doing this twice to check that we don't end up with multiple factory instances
            services.AddGrpcClient<AnotherDefaultLookingClient>(o =>
            {
                o.Address = new Uri("https://nowhere.com/");
            });
            services.AddGrpcClient<IMyCustomService>(o =>
            {
                o.Address = new Uri("https://nowhere.com/");
            });
            services.AddHttpClient("TestClient");

            // this would be via a custom extension method or similar
            if (registerCustomProvider)
            {
                // doing this twice to check that we don't end up with multiple factory instances
                services.TryAddEnumerable(ServiceDescriptor.Singleton<GrpcClientFactory, CustomGrpcClientFactory>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<GrpcClientFactory, CustomGrpcClientFactory>());
            }

            var provider = services.BuildServiceProvider();
            var factoryCount = provider.GetServices<GrpcClientFactory>().Count();
            Assert.AreEqual(registerCustomProvider ? 2 : 1, factoryCount);
            return provider;
        }

        [Theory]
        [TestCase(true)]
        [TestCase(false)]
        public void StandardServiceWorksWithOrWithoutCustomProvider(bool registerCustomProvider)
        {
            var services = SharedRegistraction(registerCustomProvider);
            var client = services.GetRequiredService<DefaultLookingClient>();
            Assert.NotNull(client);
        }

        [Test]
        public void CustomServiceFailsWithoutCustomProvider()
        {
            var services = SharedRegistraction(false);
            Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IMyCustomService>());
        }

        [Test]
        public void CustomServiceWorksWithCustomProvider()
        {
            var services = SharedRegistraction(true);
            var client = services.GetRequiredService<IMyCustomService>();
            Assert.NotNull(client);
        }

        public class DefaultLookingClient : ClientBase
        {
            public DefaultLookingClient(CallInvoker callInvoker) : base(callInvoker) { }
        }
        public class AnotherDefaultLookingClient : ClientBase
        {
            public AnotherDefaultLookingClient(CallInvoker callInvoker) : base(callInvoker) { }
        }

        interface IMyCustomService
        {
            public ValueTask<MyResponse> MyMethodAsync(MyRequest request);
        }
        class MyRequest { }
        class MyResponse { }
        class MyService : IMyCustomService
        {
            public CallInvoker CallInvoker { get; }
            public MyService(CallInvoker callInvoker) => CallInvoker = callInvoker ?? throw new ArgumentNullException(nameof(callInvoker));
            public ValueTask<MyResponse> MyMethodAsync(MyRequest request) => new ValueTask<MyResponse>(new MyResponse());
        }

        public class CustomGrpcClientFactory : GrpcClientFactory
        {
            private readonly IServiceProvider _serviceProvider;
            public CustomGrpcClientFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
            public override TClient? CreateClient<TClient>(string name) where TClient : class
            {
                // only knows how to create one thing
                if (typeof(TClient) == typeof(IMyCustomService))
                {
                    var callInvoker = GetCallInvoker<TClient>(_serviceProvider, name);
                    return (TClient)(object)new MyService(callInvoker);
                }
                return null;
            }
        }
    }
}
