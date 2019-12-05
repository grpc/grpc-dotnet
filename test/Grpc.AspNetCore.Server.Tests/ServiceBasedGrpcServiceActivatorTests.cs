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

using System;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class ServiceBasedGrpcServiceActivatorTests
    {
        public class GrpcService
        {
            public bool Disposed { get; private set; } = false;
            public void Dispose() => Disposed = true;
        }
        public class DisposableGrpcService : IDisposable
        {
            public bool Disposed { get; private set; } = false;
            public void Dispose() => Disposed = true;
        }
        public class AsyncDisposableGrpcService : DisposableGrpcService, IAsyncDisposable
        {
            public bool AsyncDisposed { get; private set; } = false;

            public ValueTask DisposeAsync()
            {
                AsyncDisposed = true;
                return default;
            }
        }

        [Test]
        public void Create_ServiceNotRegisteredInServiceLocator_ThrowsAnException()
        {
            // Arrange
            var activator = new ServiceBasedGrpcServiceActivator();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => activator.Create(CreateServerCallContext(), typeof(GrpcService)));

            // Assert
            // Assert.AreEqual("service", ex.ParamName);
        }

        [Test]
        public void Create_ServiceRegisterdInServiceLocator_CreatesTheService()
        {
            // Arrange
            var expectedService = new GrpcService();
            var context = CreateServerCallContext(CreateServiceProvider(expectedService));

            var activator = new ServiceBasedGrpcServiceActivator();

            // Act
            var actualService = activator.Create(context, typeof(GrpcService));

            // Assert
            Assert.AreSame(expectedService, actualService);
        }

        [Test]
        public async Task Release_Disposable_NeverDisposesInstance()
        {
            // Arrange
            var service = new DisposableGrpcService();

            var activator = new ServiceBasedGrpcServiceActivator();

            // Act
            await activator.ReleaseAsync(service).AsTask().DefaultTimeout();

            // Assert
            Assert.False(service.Disposed);
        }

        [Test]
        public async Task Release_AsyncDisposable_NeverDisposesInstance()
        {
            // Arrange
            var service = new AsyncDisposableGrpcService();

            var activator = new ServiceBasedGrpcServiceActivator();

            // Act
            await activator.ReleaseAsync(service).AsTask().DefaultTimeout();

            // Assert
            Assert.False(service.Disposed);
            Assert.False(service.AsyncDisposed);
        }

        private static IServiceProvider CreateServiceProvider(object grpcService)
        {
            var mockServiceProvider = new Mock<IServiceProvider>();
            Type grpcServiceType = grpcService.GetType();
            mockServiceProvider
                .Setup(sp => sp.GetService(grpcServiceType))
                .Returns(grpcService);

            return mockServiceProvider.Object;
        }

        private static HttpContextServerCallContext CreateServerCallContext(IServiceProvider? serviceProvider = null)
        {
            var httpContext = new Mock<HttpContext>();

            httpContext
                .Setup(context => context.RequestServices)
                .Returns(serviceProvider ?? new Mock<IServiceProvider>().Object);
            
            return new HttpContextServerCallContext(httpContext.Object, null!, null!);
        }
    }
}
