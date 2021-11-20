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

using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Shared.Server;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class UnaryServerCallHandlerTests
    {
        private static readonly Marshaller<TestMessage> _marshaller = new Marshaller<TestMessage>((message, context) => { context.Complete(Array.Empty<byte>()); }, context => new TestMessage());

        [Test]
        public void Invoke_ThrowException_ReleaseCalledAndErrorThrown()
        {
            // Arrange
            var serviceActivator = new TestGrpcServiceActivator<TestService>();
            var ex = new Exception("Exception!");
            var invoker = new UnaryServerMethodInvoker<TestService, TestMessage, TestMessage>(
                (service, reader, context) => throw ex,
                new Method<TestMessage, TestMessage>(MethodType.Unary, "test", "test", _marshaller, _marshaller),
                HttpContextServerCallContextHelper.CreateMethodOptions(),
                serviceActivator);
            var httpContext = HttpContextHelpers.CreateContext();

            // Act
            var task = invoker.Invoke(httpContext, HttpContextServerCallContextHelper.CreateServerCallContext(), new TestMessage());

            // Assert
            Assert.True(serviceActivator.Released);
            Assert.True(task.IsFaulted);
            Assert.AreEqual(ex, task.Exception!.InnerException);
        }

        [Test]
        public async Task Invoke_ThrowExceptionAwaitedRelease_ReleaseCalledAndErrorThrown()
        {
            // Arrange
            var releaseTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var serviceActivator = new TcsGrpcServiceActivator<TestService>(releaseTcs);
            var thrownException = new Exception("Exception!");
            var invoker = new UnaryServerMethodInvoker<TestService, TestMessage, TestMessage>(
                (service, reader, context) => throw thrownException,
                new Method<TestMessage, TestMessage>(MethodType.Unary, "test", "test", _marshaller, _marshaller),
                HttpContextServerCallContextHelper.CreateMethodOptions(),
                serviceActivator);
            var httpContext = HttpContextHelpers.CreateContext();

            // Act
            var task = invoker.Invoke(httpContext, HttpContextServerCallContextHelper.CreateServerCallContext(), new TestMessage());
            Assert.False(task.IsCompleted);

            releaseTcs.SetResult(null);

            try
            {
                await task;
                Assert.Fail();
            }
            catch (Exception ex)
            {
                // Assert
                Assert.True(serviceActivator.Released);
                Assert.AreEqual(thrownException, ex);
            }
        }

        [Test]
        public async Task Invoke_SuccessAwaitedRelease_ReleaseCalled()
        {
            // Arrange
            var releaseTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var serviceActivator = new TcsGrpcServiceActivator<TestService>(releaseTcs);
            var invoker = new UnaryServerMethodInvoker<TestService, TestMessage, TestMessage>(
                (service, reader, context) => Task.FromResult(new TestMessage()),
                new Method<TestMessage, TestMessage>(MethodType.Unary, "test", "test", _marshaller, _marshaller),
                HttpContextServerCallContextHelper.CreateMethodOptions(),
                serviceActivator);
            var httpContext = HttpContextHelpers.CreateContext();

            // Act
            var task = invoker.Invoke(httpContext, HttpContextServerCallContextHelper.CreateServerCallContext(), new TestMessage());
            Assert.False(task.IsCompleted);

            releaseTcs.SetResult(null);
            await task;

            // Assert
            Assert.True(serviceActivator.Released);
        }

        [Test]
        public async Task Invoke_AwaitedSuccess_ReleaseCalled()
        {
            // Arrange
            var methodTcs = new TaskCompletionSource<TestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var methodResult = new TestMessage();
            var serviceActivator = new TestGrpcServiceActivator<TestService>();
            var invoker = new UnaryServerMethodInvoker<TestService, TestMessage, TestMessage>(
                (service, reader, context) => methodTcs.Task,
                new Method<TestMessage, TestMessage>(MethodType.Unary, "test", "test", _marshaller, _marshaller),
                HttpContextServerCallContextHelper.CreateMethodOptions(),
                serviceActivator);
            var httpContext = HttpContextHelpers.CreateContext();

            // Act
            var task = invoker.Invoke(httpContext, HttpContextServerCallContextHelper.CreateServerCallContext(), new TestMessage());
            Assert.False(task.IsCompleted);

            methodTcs.SetResult(methodResult);
            var awaitedResult = await task;

            // Assert
            Assert.AreEqual(methodResult, awaitedResult);
            Assert.True(serviceActivator.Released);
        }

        private class TcsGrpcServiceActivator<TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class, new()
        {
            private readonly TaskCompletionSource<object?> _tcs;

            public bool Released { get; private set; }

            public TcsGrpcServiceActivator(TaskCompletionSource<object?> tcs)
            {
                _tcs = tcs;
            }

            public GrpcActivatorHandle<TGrpcService> Create(IServiceProvider serviceProvider)
            {
                return new GrpcActivatorHandle<TGrpcService>(new TGrpcService(), false, null);
            }

            public ValueTask ReleaseAsync(GrpcActivatorHandle<TGrpcService> service)
            {
                Released = true;
                return new ValueTask(_tcs.Task);
            }
        }
    }
}
