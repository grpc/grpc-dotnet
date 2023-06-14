#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Globalization;
using System.Net;
using Greet;
using Grpc.Core;
#if NET5_0_OR_GREATER
using Grpc.Net.Client.Balancer;
#endif
using Grpc.Net.ClientFactory;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests;

[TestFixture]
public class GrpcHttpClientBuilderExtensionsTests
{
    [Test]
    public async Task ConfigureChannel_MaxSizeSet_ThrowMaxSizeError()
    {
        // Arrange
        var request = new HelloRequest
        {
            Name = new string('!', 1024)
        };

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigureChannel(options =>
            {
                options.MaxSendMessageSize = 100;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // Handle bad response
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(request).ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
        Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
    }

    private class TestService
    {
        public TestService(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    [Test]
    public async Task AddInterceptor_MultipleInstances_ExecutedInOrder()
    {
        // Arrange
        var list = new List<int>();
        var testHttpMessageHandler = new TestHttpMessageHandler();

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddInterceptor(() => new CallbackInterceptor(o => list.Add(1)))
            .AddInterceptor(() => new CallbackInterceptor(o => list.Add(2)))
            .AddInterceptor(() => new CallbackInterceptor(o => list.Add(3)))
            .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.IsTrue(testHttpMessageHandler.Invoked);
        Assert.IsNotNull(response);
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(1, list[0]);
        Assert.AreEqual(2, list[1]);
        Assert.AreEqual(3, list[2]);
    }

    [Test]
    public void AddInterceptor_OnGrpcClientFactoryWithServicesConfig_Success()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<CallbackInterceptor>(s => new CallbackInterceptor(o => { }));

        // Act
        services
            .AddGrpcClient<Greeter.GreeterClient>((s, o) => o.Address = new Uri("http://localhost"))
            .AddInterceptor<CallbackInterceptor>()
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        // Assert
        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsNotNull(serviceProvider.GetRequiredService<Greeter.GreeterClient>());
    }

    [Test]
    public void AddInterceptor_NotFromGrpcClientFactoryAndExistingGrpcClient_ThrowError()
    {
        // Arrange
        var services = new ServiceCollection();
        var client = services.AddHttpClient("TestClient");

        var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })))!;
        Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

        ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })))!;
        Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
    }

    [Test]
    public void AddInterceptor_AddGrpcClientWithoutConfig_NoError()
    {
        // Arrange
        var services = new ServiceCollection();
        var client = services.AddGrpcClient<Greeter.GreeterClient>();

        // Act
        client.AddInterceptor(() => new CallbackInterceptor(o => { }));
    }

    [Test]
    public void AddInterceptor_AddGrpcClientWithNameAndWithoutConfig_NoError()
    {
        // Arrange
        var services = new ServiceCollection();
        var client = services.AddGrpcClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // Act
        client.AddInterceptor(() => new CallbackInterceptor(o => { }));
    }

    [Test]
    public void AddInterceptor_NotFromGrpcClientFactory_ThrowError()
    {
        // Arrange
        var services = new ServiceCollection();
        var client = services.AddHttpClient("TestClient");

        var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })))!;
        Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

        ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })))!;
        Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
    }

    [Test]
    public async Task AddInterceptorGeneric_MultipleInstances_ExecutedInOrder()
    {
        // Arrange
        var list = new List<int>();
        var i = 0;

        var services = new ServiceCollection();
        services.AddTransient<CallbackInterceptor>(s => new CallbackInterceptor(o =>
        {
            var increment = i += 2;
            list.Add(increment);
        }));
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddInterceptor<CallbackInterceptor>()
            .AddInterceptor<CallbackInterceptor>()
            .AddInterceptor<CallbackInterceptor>()
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.IsNotNull(response);
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(2, list[0]);
        Assert.AreEqual(4, list[1]);
        Assert.AreEqual(6, list[2]);
    }

    [Test]
    public void AddInterceptorGeneric_NotFromGrpcClientFactory_ThrowError()
    {
        // Arrange
        var services = new ServiceCollection();
        var client = services.AddHttpClient("TestClient");

        var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor<CallbackInterceptor>())!;
        Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
    }

    [Test]
    public void ConfigureGrpcClientCreator_CreatorSuccess_ClientReturned()
    {
        // Arrange
        Greeter.GreeterClient? createdClient = null;

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigureGrpcClientCreator(callInvoker =>
            {
                createdClient = new Greeter.GreeterClient(callInvoker);
                return createdClient;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // Assert
        Assert.IsNotNull(client);
        Assert.AreEqual(createdClient, client);
    }

    [Test]
    public void ConfigureGrpcClientCreator_ServiceProviderCreatorSuccess_ClientReturned()
    {
        // Arrange
        DerivedGreeterClient? createdGreaterClient = null;

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigureGrpcClientCreator((serviceProvider, callInvoker) =>
            {
                createdGreaterClient = new DerivedGreeterClient(callInvoker);
                return createdGreaterClient;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());
        services
            .AddGrpcClient<SecondGreeter.SecondGreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var greeterClient = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
        var secondGreeterClient = clientFactory.CreateClient<SecondGreeter.SecondGreeterClient>(nameof(SecondGreeter.SecondGreeterClient));

        // Assert
        Assert.IsNotNull(greeterClient);
        Assert.AreEqual(createdGreaterClient, greeterClient);
        Assert.IsNotNull(secondGreeterClient);
    }

    [Test]
    public void ConfigureGrpcClientCreator_CreatorReturnsNull_ThrowError()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigureGrpcClientCreator(callInvoker =>
            {
                return null!;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)))!;

        // Assert
        Assert.AreEqual("A null instance was returned by the configured client creator.", ex.Message);
    }

    [Test]
    public void ConfigureGrpcClientCreator_CreatorReturnsIncorrectType_ThrowError()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .ConfigureGrpcClientCreator(callInvoker =>
            {
                return new object();
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)))!;

        // Assert
        Assert.AreEqual("The System.Object instance returned by the configured client creator is not compatible with Greet.Greeter+GreeterClient.", ex.Message);
    }

    [TestCase(InterceptorScope.Client, 2)]
    [TestCase(InterceptorScope.Channel, 1)]
    public async Task AddInterceptor_InterceptorLifetime_InterceptorCreatedCountCorrect(InterceptorScope scope, int callCount)
    {
        // Arrange
        var testHttpMessageHandler = new TestHttpMessageHandler();

        var interceptorCreatedCount = 0;
        var services = new ServiceCollection();
        services.AddTransient<CallbackInterceptor>(s =>
        {
            interceptorCreatedCount++;
            return new CallbackInterceptor(o => { });
        });

        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddInterceptor<CallbackInterceptor>(scope)
            .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();

        var client1 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
        await client1.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

        var client2 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
        await client2.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(callCount, interceptorCreatedCount);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task AddInterceptor_ClientLifetimeInScope_InterceptorCreatedCountCorrect(int scopes)
    {
        // Arrange
        var testHttpMessageHandler = new TestHttpMessageHandler();

        var interceptorCreatedCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<CallbackInterceptor>(s =>
        {
            interceptorCreatedCount++;
            return new CallbackInterceptor(o => { });
        });

        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddInterceptor<CallbackInterceptor>(InterceptorScope.Client)
            .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        for (var i = 0; i < scopes; i++)
        {
            await MakeCallsInScope(serviceProvider);
        }

        // Assert
        Assert.AreEqual(scopes, interceptorCreatedCount);

        static async Task MakeCallsInScope(ServiceProvider rootServiceProvider)
        {
            var scope = rootServiceProvider.CreateScope();

            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

            var client1 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
            await client1.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            var client2 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
            await client2.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();
        }
    }

    [Test]
    public async Task AddInterceptorGeneric_ScopedLifetime_CreatedOncePerScope()
    {
        // Arrange
        var i = 0;
        var channelInterceptorCreatedCount = 0;

        var services = new ServiceCollection();
        services.AddScoped<List<int>>();
        services.AddScoped<CallbackInterceptor>(s =>
        {
            var increment = ++i;
            return new CallbackInterceptor(o =>
            {
                var list = s.GetRequiredService<List<int>>();
                list.Add(increment * list.Count);
            });
        });
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddInterceptor(() =>
            {
                channelInterceptorCreatedCount++;
                return new CallbackInterceptor(o => { });
            })
            .AddInterceptor<CallbackInterceptor>(InterceptorScope.Client)
            .AddInterceptor<CallbackInterceptor>(InterceptorScope.Client)
            .AddInterceptor<CallbackInterceptor>(InterceptorScope.Client)
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        using (var scope = serviceProvider.CreateScope())
        {
            var list = scope.ServiceProvider.GetRequiredService<List<int>>();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(0, list[0]);
            Assert.AreEqual(1, list[1]);
            Assert.AreEqual(2, list[2]);
        }

        // Act
        using (var scope = serviceProvider.CreateScope())
        {
            var list = scope.ServiceProvider.GetRequiredService<List<int>>();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(0, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(4, list[2]);
        }

        // Only one channel and its interceptor is created for multiple scopes.
        Assert.AreEqual(1, channelInterceptorCreatedCount);
    }

    [Test]
    public async Task AddCallCredentials_ServiceProvider_RunInScope()
    {
        // Arrange
        var scopeCount = 0;
        var authHeaderValues = new List<string>();

        var services = new ServiceCollection();
        services
            .AddScoped<AuthProvider>(s => new AuthProvider((scopeCount++).ToString(CultureInfo.InvariantCulture)))
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("https://localhost");
            })
            .AddCallCredentials(async (context, metadata, serviceProvider) =>
            {
                var authProvider = serviceProvider.GetRequiredService<AuthProvider>();
                metadata.Add("authorize", await authProvider.GetTokenAsync());
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(request =>
            {
                if (request.Headers.TryGetValues("authorize", out var values))
                {
                    authHeaderValues.AddRange(values);
                }
            }));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        using (var scope = serviceProvider.CreateScope())
        {
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response1 = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();
            var response2 = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response1);
            Assert.IsNotNull(response2);
            Assert.AreEqual(2, authHeaderValues.Count);
            Assert.AreEqual("0", authHeaderValues[0]);
            Assert.AreEqual("0", authHeaderValues[1]);
        }

        authHeaderValues.Clear();

        // Act
        using (var scope = serviceProvider.CreateScope())
        {
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response1 = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();
            var response2 = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response1);
            Assert.IsNotNull(response2);
            Assert.AreEqual(2, authHeaderValues.Count);
            Assert.AreEqual("1", authHeaderValues[0]);
            Assert.AreEqual("1", authHeaderValues[1]);
        }

        // Only one channel and its interceptor is created for multiple scopes.
        Assert.AreEqual(2, scopeCount);
    }

    [Test]
    public async Task AddCallCredentials_CallCredentials_HeaderAdded()
    {
        // Arrange
        HttpRequestMessage? sentRequest = null;

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("https://localhost");
            })
            .AddCallCredentials(CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("factory-authorize", "auth!");
                return Task.CompletedTask;
            }))
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(request =>
            {
                sentRequest = request;
            }));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        var response = await client.SayHelloAsync(
            new HelloRequest(),
            new CallOptions()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.NotNull(response);

        Assert.AreEqual("auth!", sentRequest!.Headers.GetValues("factory-authorize").Single());
    }

    [Test]
    public void AddCallCredentials_InsecureChannel_Error()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddCallCredentials(CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("factory-authorize", "auth!");
                return Task.CompletedTask;
            }))
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)))!;

        // Assert
        Assert.AreEqual("Call credential configured for gRPC client 'GreeterClient' requires TLS, and the client isn't configured to use TLS. " +
            "Either configure a TLS address, or use the call credential without TLS by setting GrpcChannelOptions.UnsafeUseInsecureChannelCallCredentials to true: " +
            "client.AddCallCredentials((context, metadata) => {}).ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true)", ex.Message);
    }

#if NET5_0_OR_GREATER
    [Test]
    public void AddCallCredentials_StaticLoadBalancingSecureChannel_Success()
    {
        // Arrange
        HttpRequestMessage? sentRequest = null;

        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
        {
            new BalancerAddress("localhost", 80)
        }));

        // Can't use ConfigurePrimaryHttpMessageHandler with load balancing because underlying
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("static:///localhost");
            })
            .ConfigureChannel(o =>
            {
                o.Credentials = ChannelCredentials.SecureSsl;
            })
            .AddCallCredentials(CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("factory-authorize", "auth!");
                return Task.CompletedTask;
            }))
            .AddHttpMessageHandler(() => new TestHttpMessageHandler(request =>
            {
                sentRequest = request;
            }));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act & Assert
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        _ = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        // No call because there isn't an endpoint at localhost:80
    }
#endif

    [Test]
    public async Task AddCallCredentials_InsecureChannel_UnsafeUseInsecureChannelCallCredentials_Success()
    {
        // Arrange
        HttpRequestMessage? sentRequest = null;

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("http://localhost");
            })
            .AddCallCredentials(CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("factory-authorize", "auth!");
                return Task.CompletedTask;
            }))
            .ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true)
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(request =>
            {
                sentRequest = request;
            }));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        var response = await client.SayHelloAsync(
            new HelloRequest(),
            new CallOptions()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.NotNull(response);

        Assert.AreEqual("auth!", sentRequest!.Headers.GetValues("factory-authorize").Single());
    }

    [Test]
    public async Task AddCallCredentials_PassedInCallCredentials_Combine()
    {
        // Arrange
        HttpRequestMessage? sentRequest = null;

        var services = new ServiceCollection();
        services
            .AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.Address = new Uri("https://localhost");
            })
            .ConfigureChannel(c =>
            {
                c.Credentials = ChannelCredentials.Create(ChannelCredentials.SecureSsl, CallCredentials.FromInterceptor((c, m) =>
                {
                    m.Add("channel-authorize", "auth!");
                    return Task.CompletedTask;
                }));
            })
            .AddCallCredentials((context, metadata) =>
            {
                metadata.Add("factory-authorize", "auth!");
                return Task.CompletedTask;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(request =>
            {
                sentRequest = request;
            }));

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Act
        var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
        var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

        var response = await client.SayHelloAsync(
            new HelloRequest(),
            new CallOptions(credentials: CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("call-authorize", "auth!");
                return Task.CompletedTask;
            }))).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.NotNull(response);

        Assert.AreEqual("auth!", sentRequest!.Headers.GetValues("channel-authorize").Single());
        Assert.AreEqual("auth!", sentRequest!.Headers.GetValues("factory-authorize").Single());
        Assert.AreEqual("auth!", sentRequest!.Headers.GetValues("call-authorize").Single());
    }

    private class AuthProvider
    {
        private readonly string _headerValue;

        public AuthProvider(string headerValue)
        {
            _headerValue = headerValue;
        }

        public Task<string> GetTokenAsync() => Task.FromResult(_headerValue);
    }

    private class DerivedGreeterClient : Greeter.GreeterClient
    {
        public DerivedGreeterClient(CallInvoker callInvoker) : base(callInvoker)
        {
        }
    }

    private class TestHttpMessageHandler : DelegatingHandler
    {
        public bool Invoked { get; private set; }

        private Action<HttpRequestMessage>? _requestCallback;

        public TestHttpMessageHandler(Action<HttpRequestMessage>? requestCallback = null)
        {
            _requestCallback = requestCallback;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Invoked = true;
            _requestCallback?.Invoke(request);

            // Get stream from request content so gRPC client serializes request message
#if NET5_0_OR_GREATER
            _ = await request.Content!.ReadAsStreamAsync(cancellationToken);
#else
            _ = await request.Content!.ReadAsStreamAsync();
#endif

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }
    }
}
