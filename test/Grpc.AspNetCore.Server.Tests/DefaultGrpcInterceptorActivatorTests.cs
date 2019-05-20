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
using System.Threading;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core.Interceptors;
using Moq;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class DefaultGrpcInterceptorActivatorTests
    {
        public class GrpcInterceptor : Interceptor
        {
            public GrpcInterceptor() { }

            public GrpcInterceptor(int x)
            {
                X = x;
            }

            public int X { get; }
            public bool Disposed { get; private set; } = false;
            public void Dispose() => Disposed = true;
        }

        public class DisposableGrpcInterceptor : Interceptor, IDisposable
        {
            public bool Disposed { get; private set; } = false;
            public void Dispose() => Disposed = true;
        }

        [Test]
        public void GrpcInterceptorCreatedIfNotResolvedFromServiceProvider()
        {
            Assert.NotNull(
                new DefaultGrpcInterceptorActivator<GrpcInterceptor>(Mock.Of<IServiceProvider>()).Create());
        }

        [Test]
        public void GrpcInterceptorCanBeResolvedFromServiceProvider()
        {
            var interceptor = new GrpcInterceptor();
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(GrpcInterceptor)))
                .Returns(interceptor);

            Assert.AreSame(interceptor,
                new DefaultGrpcInterceptorActivator<GrpcInterceptor>(mockServiceProvider.Object).Create());
        }

        [Test]
        public void GrpcInterceptorCanResolveArgumentsFromArg()
        {
            var mutex = new Mutex();
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(Mutex)))
                .Returns(mutex);

            var interceptor = (GrpcInterceptor)new DefaultGrpcInterceptorActivator<GrpcInterceptor>(mockServiceProvider.Object).Create(10);

            Assert.AreEqual(10, interceptor.X);
        }

        [Test]
        public void GrpcInterceptorNotResolvedFromServiceProviderIfExplicitArgsGiven()
        {
            var interceptor = new GrpcInterceptor();
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(GrpcInterceptor)))
                .Returns(interceptor);

            var activatedInstance = new DefaultGrpcInterceptorActivator<GrpcInterceptor>(mockServiceProvider.Object).Create(10);

            Assert.AreNotSame(interceptor, activatedInstance);
            Assert.AreEqual(10, ((GrpcInterceptor)activatedInstance).X);
        }

        [Test]
        public void DisposeNotCalledForServicesResolvedFromServiceProvider()
        {
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(DisposableGrpcInterceptor)))
                .Returns(() =>
                {
                    return new DisposableGrpcInterceptor();
                });

            var interceptorActivator = new DefaultGrpcInterceptorActivator<DisposableGrpcInterceptor>(mockServiceProvider.Object);
            var interceptor = (DisposableGrpcInterceptor)interceptorActivator.Create();
            interceptorActivator.Release(interceptor);

            Assert.False(interceptor.Disposed);
        }

        [Test]
        public void DisposeCalledForDisposableServicesCreatedByActivator()
        {
            var interceptorActivator = new DefaultGrpcInterceptorActivator<DisposableGrpcInterceptor>(Mock.Of<IServiceProvider>());
            var interceptor = (DisposableGrpcInterceptor)interceptorActivator.Create();
            interceptorActivator.Release(interceptor);

            Assert.True(interceptor.Disposed);
        }

        [Test]
        public void DisposeCalledForMultipleDisposableServicesCreatedByActivator()
        {
            var interceptorActivator = new DefaultGrpcInterceptorActivator<DisposableGrpcInterceptor>(Mock.Of<IServiceProvider>());
            var interceptor1 = (DisposableGrpcInterceptor)interceptorActivator.Create();
            var interceptor2 = (DisposableGrpcInterceptor)interceptorActivator.Create();
            var interceptor3 = (DisposableGrpcInterceptor)interceptorActivator.Create();
            interceptorActivator.Release(interceptor3);
            interceptorActivator.Release(interceptor2);
            interceptorActivator.Release(interceptor1);

            Assert.True(interceptor1.Disposed);
            Assert.True(interceptor2.Disposed);
            Assert.True(interceptor3.Disposed);
        }

        [Test]
        public void DisposeNotCalledForUndisposableServicesCreatedByActivator()
        {
            var interceptorActivator = new DefaultGrpcInterceptorActivator<GrpcInterceptor>(Mock.Of<IServiceProvider>());
            var interceptor = (GrpcInterceptor)interceptorActivator.Create();
            interceptorActivator.Release(interceptor);

            Assert.False(interceptor.Disposed);
        }

        [Test]
        public void DisposeNotCalledForDisposableServicesNotCreatedByActivator()
        {
            var interceptorActivator = new DefaultGrpcInterceptorActivator<DisposableGrpcInterceptor>(Mock.Of<IServiceProvider>());
            var interceptor = (DisposableGrpcInterceptor)interceptorActivator.Create();
            var anotherInterceptor = new DisposableGrpcInterceptor();
            interceptorActivator.Release(anotherInterceptor);

            Assert.False(interceptor.Disposed);
            Assert.False(anotherInterceptor.Disposed);
        }

        [Test]
        public void CannotReleaseNullService()
        {
            Assert.AreEqual("interceptor",
                Assert.Throws<ArgumentNullException>(
                    () => new DefaultGrpcInterceptorActivator<GrpcInterceptor>(Mock.Of<IServiceProvider>()).Release(null!)).ParamName);
        }
    }
}
