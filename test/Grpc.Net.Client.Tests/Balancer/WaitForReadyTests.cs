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

#if SUPPORT_LOAD_BALANCING
using System;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Testing;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Collections.Generic;
using Grpc.Net.Client.Balancer.Internal;
using System.IO;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Net.Client.Balancer;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class WaitForReadyTests
{
    [Test]
    public async Task ResolverReturnsNoAddresses_CallWithWaitForReady_Wait()
    {
        // Arrange
        string? authority = null;
        var testMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            authority = request.RequestUri!.Authority;
            var reply = new HelloReply { Message = "Hello world" };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var services = new ServiceCollection();
        services.AddNUnitLogger();

        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory>(s => new TestResolverFactory(s.GetRequiredService<TestResolver>()));
        services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<TestResolver>();

        var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = serviceProvider;
        });

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());

        await resolver.HasResolvedTask.DefaultTimeout();

        var responseTask = call.ResponseAsync;

        Assert.IsFalse(responseTask.IsCompleted);
        Assert.IsNull(authority);

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 81)
        });

        await responseTask.DefaultTimeout();
        Assert.AreEqual("localhost:81", authority);
    }

    [Test]
    public async Task ResolverReturnsNoAddresses_DeadlineWhileWaitForReady_Error()
    {
        // Arrange
        var testMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var services = new ServiceCollection();
        var resolver = new TestResolver();
        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

        var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        // Act
        var callOptions = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(0.2)).WithWaitForReady();
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, callOptions, new HelloRequest());

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
        Assert.AreEqual(string.Empty, ex.Status.Detail);
    }

    [Test]
    public async Task ResolverReturnsNoAddresses_DisposeWhileWaitForReady_Error()
    {
        // Arrange
        var testMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var services = new ServiceCollection();
        var resolver = new TestResolver();
        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

        var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        // Act
        var callOptions = new CallOptions().WithWaitForReady();
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, callOptions, new HelloRequest());

        var exTask = ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        call.Dispose();

        var ex = await exTask.DefaultTimeout();

        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);
    }
}

#endif
