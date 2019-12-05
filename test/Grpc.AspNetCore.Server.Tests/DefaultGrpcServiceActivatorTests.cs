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
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class DefaultGrpcServiceActivatorTests
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
        public void Create_Always_CreatedByActivator()
        {
            // Arrange
            var activator = new DefaultGrpcServiceActivator();

            // Act
            var service = activator.Create(CreateServerCallContext(), typeof(GrpcService));

            // Assert
            Assert.NotNull(service);
            Assert.IsInstanceOf<GrpcService>(service);
        }

        [Test]
        public async Task Release_Always_DisposeCalled()
        {
            // Arrange
            var serviceActivator = new DefaultGrpcServiceActivator();

            var service = new DisposableGrpcService();

            // Act
            await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

            // Assert
            Assert.True(service.Disposed);
        }

        [Test]
        public async Task Release_Always_DisposeAsyncCalled()
        {
            // Arrange
            var serviceActivator = new DefaultGrpcServiceActivator();

            var service = new AsyncDisposableGrpcService();

            // Act
            await serviceActivator.ReleaseAsync(service).AsTask().DefaultTimeout();

            // Assert
            Assert.False(service.Disposed);
            Assert.True(service.AsyncDisposed);
        }

        [Test]
        public async Task Release_NullService_ThrowError()
        {
            // Arrange
            var serviceActivator = new DefaultGrpcServiceActivator();

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<ArgumentException>(() => serviceActivator.ReleaseAsync(null!).AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("grpcServiceInstance", ex.ParamName);
        }

        HttpContextServerCallContext CreateServerCallContext()
        {
            var httpContext = new Mock<HttpContext>().Object;
            httpContext.RequestServices = new Mock<IServiceProvider>().Object;
            return new HttpContextServerCallContext(httpContext, null!, null!);
        }
    }
}
