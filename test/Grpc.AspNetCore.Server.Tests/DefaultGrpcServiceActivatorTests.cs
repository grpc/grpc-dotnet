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

using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class DefaultGrpcServiceActivatorTests
{
    public class GrpcService
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public class DisposableGrpcService : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public class AsyncDisposableGrpcService : DisposableGrpcService, IAsyncDisposable
    {
        public bool AsyncDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            AsyncDisposed = true;
            return default;
        }
    }
#if NET8_0_OR_GREATER
    public class GrpcServiceWithKeyedService
    {
        public GrpcServiceWithKeyedService([FromKeyedServices("test")] KeyedClass c)
        {
            C = c;
        }

        public KeyedClass C { get; }
    }

    public class KeyedClass
    {
        public required string Key { get; init; }
    }
#endif

    [Test]
    public void Create_NotResolvedFromServiceProvider_CreatedByActivator()
    {
        // Arrange
        var activator = new DefaultGrpcServiceActivator<GrpcService>();

        // Act
        var handle = activator.Create(Mock.Of<IServiceProvider>());

        // Assert
        Assert.NotNull(handle.Instance);
        Assert.IsTrue(handle.Created);
    }

#if NET8_0_OR_GREATER
    [Test]
    public void Create_KeyedService_CreatedByActivator()
    {
        // Arrange
        var activator = new DefaultGrpcServiceActivator<GrpcServiceWithKeyedService>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("test", new KeyedClass { Key = "test" });
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var handle = activator.Create(serviceProvider);

        // Assert
        var interceptor = handle.Instance;
        Assert.AreEqual("test", interceptor.C.Key);
    }
#endif

    [Test]
    public void Create_ResolvedFromServiceProvider_NotCreatedByActivator()
    {
        // Arrange
        var service = new GrpcService();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(GrpcService)))
            .Returns(service);

        // Act
        var handle = new DefaultGrpcServiceActivator<GrpcService>().Create(mockServiceProvider.Object);

        // Assert
        Assert.AreSame(service, handle.Instance);
        Assert.IsFalse(handle.Created);
    }

    [Test]
    public async Task Release_DisposableResolvedFromServiceProvider_DisposeNotCalled()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(DisposableGrpcService)))
            .Returns(() =>
            {
                return new DisposableGrpcService();
            });

        var serviceActivator = new DefaultGrpcServiceActivator<DisposableGrpcService>();
        var service = serviceActivator.Create(mockServiceProvider.Object);

        // Act
        await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

        // Assert
        Assert.False(service.Instance.Disposed);
    }

    [Test]
    public async Task Release_DisposableCreatedByActivator_DisposeCalled()
    {
        // Arrange
        var serviceActivator = new DefaultGrpcServiceActivator<DisposableGrpcService>();
        var service = serviceActivator.Create(Mock.Of<IServiceProvider>());

        // Act
        await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

        // Assert
        Assert.True(service.Instance.Disposed);
    }

    [Test]
    public async Task Release_AsyncDisposableCreatedByActivator_DisposeAsyncCalled()
    {
        // Arrange
        var serviceActivator = new DefaultGrpcServiceActivator<AsyncDisposableGrpcService>();
        var service = serviceActivator.Create(Mock.Of<IServiceProvider>());

        // Act
        await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

        // Assert
        Assert.False(service.Instance.Disposed);
        Assert.True(service.Instance.AsyncDisposed);
    }

    [Test]
    public async Task Release_NonDisposableCreatedByActivator_DisposeNotCalled()
    {
        // Arrange
        var serviceActivator = new DefaultGrpcServiceActivator<GrpcService>();
        var service = serviceActivator.Create(Mock.Of<IServiceProvider>());

        // Act
        await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

        // Assert
        Assert.False(service.Instance.Disposed);
    }

    [Test]
    public async Task Release_NullService_ThrowError()
    {
        // Arrange
        var activator = new DefaultGrpcServiceActivator<GrpcService>();

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<ArgumentException>(() => activator.ReleaseAsync(new GrpcActivatorHandle<GrpcService>(null!, created: true, state: null)).AsTask()).DefaultTimeout();

        // Assert
        Assert.AreEqual("service", ex.ParamName);
    }
}
