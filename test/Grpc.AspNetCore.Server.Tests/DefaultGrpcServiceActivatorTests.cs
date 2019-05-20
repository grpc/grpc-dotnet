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
using Grpc.AspNetCore.Server.Internal;
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

        [Test]
        public void GrpcServiceCreatedIfNotResolvedFromServiceProvider()
        {
            Assert.NotNull(
                new DefaultGrpcServiceActivator<GrpcService>(Mock.Of<IServiceProvider>()).Create());
        }

        [Test]
        public void GrpcServiceCanBeResolvedFromServiceProvider()
        {
            var service = new GrpcService();
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(GrpcService)))
                .Returns(service);

            Assert.AreSame(service,
                new DefaultGrpcServiceActivator<GrpcService>(mockServiceProvider.Object).Create());
        }

        [Test]
        public void DisposeNotCalledForServicesResolvedFromServiceProvider()
        {
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(DisposableGrpcService)))
                .Returns(() =>
                {
                    return new DisposableGrpcService();
                });

            var serviceActivator = new DefaultGrpcServiceActivator<DisposableGrpcService>(mockServiceProvider.Object);
            var service = serviceActivator.Create();
            serviceActivator.Release(service);

            Assert.False(service.Disposed);
        }

        [Test]
        public void DisposeCalledForDisposableServicesCreatedByActivator()
        {
            var serviceActivator = new DefaultGrpcServiceActivator<DisposableGrpcService>(Mock.Of<IServiceProvider>());
            var service = serviceActivator.Create();
            serviceActivator.Release(service);

            Assert.True(service.Disposed);
        }

        [Test]
        public void DisposeNotCalledForUndisposableServicesCreatedByActivator()
        {
            var serviceActivator = new DefaultGrpcServiceActivator<GrpcService>(Mock.Of<IServiceProvider>());
            var service = serviceActivator.Create();
            serviceActivator.Release(service);

            Assert.False(service.Disposed);
        }

        [Test]
        public void CannotReleaseNullService()
        {
            Assert.AreEqual("service",
                Assert.Throws<ArgumentNullException>(
                    () => new DefaultGrpcServiceActivator<GrpcService>(Mock.Of<IServiceProvider>()).Release(null!)).ParamName);
        }
    }
}
