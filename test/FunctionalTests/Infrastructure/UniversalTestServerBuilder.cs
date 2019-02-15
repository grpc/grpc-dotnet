using System;
using FunctionalTestsWebsite.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class UniversalTestServerBuilder
    {
        public class ServiceRegistor
        {
            private readonly IEndpointRouteBuilder _routeBuilder;
            public ServiceRegistor(IEndpointRouteBuilder routeBuilder)
            {
                _routeBuilder = routeBuilder;
            }

            public void Register<TService>(Func<TService> create) where TService : class
            {
                _routeBuilder.MapGrpcService<TService>(ctx => create());
            }
        }

        private readonly Action<ServiceRegistor> _register;

        private UniversalTestServerBuilder(Action<ServiceRegistor> register)
        {
            _register = register;
        }

        public static UniversalTestServerBuilder WithServices(Action<ServiceRegistor> register)
        {
            if (register == null)
            {
                throw new ArgumentNullException(nameof(register));
            }
            var instance = new UniversalTestServerBuilder(register);
            return instance;
        }

        public TestServer Build()
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddGrpc();
                })
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        // Workaround for https://github.com/aspnet/AspNetCore/issues/6880
                        if (!context.Response.SupportsTrailers())
                        {
                            context.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature
                            {
                                Trailers = new HttpResponseTrailers()
                            });
                        }

                        // Workaround for https://github.com/aspnet/AspNetCore/issues/7449
                        context.Features.Set<IHttpRequestLifetimeFeature>(new TestHttpRequestLifetimeFeature());

                        return next();
                    });

                    app.UseRouting(b =>
                    {
                        var registor = new ServiceRegistor(b);
                        _register(registor);
                    });
                });

            var server = new TestServer(builder);
            server.BaseAddress = new System.Uri("http://localhost:5002");
            return server;
        }
    }
}
