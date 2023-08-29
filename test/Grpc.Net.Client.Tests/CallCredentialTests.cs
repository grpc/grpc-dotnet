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

using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class CallCredentialTests
{
    [Test]
    public async Task CallCredentialsWithHttps_WhenAsyncAuthInterceptorThrow_ShouldThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

        // Act
        var expectedException = new Exception("Some AsyncAuthInterceptor Exception");

        var callCredentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            return Task.FromException(expectedException);
        });

        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreSame(expectedException, ex.Status.DebugException);
    }

    [Test]
    public async Task CallCredentialsWithHttps_MetadataOnRequest()
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            // The operation is asynchronous to ensure auth interceptor is awaited.
            // Sending the request and returning a response is blocked until the auth interceptor completes.
            await syncPoint.WaitToContinue();

            // Set header.
            metadata.Add("authorization", "SECRET_TOKEN");
        });
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        var responseTask = call.ResponseAsync;

        await syncPoint.WaitForSyncPoint().DefaultTimeout();

        // Response task should be blocked waiting for the auth interceptor to complete.
        Assert.False(responseTask.IsCompleted);
        // Sending the request should be blocked waiting for the auth interceptor to complete.
        Assert.Null(authorizationValue);

        syncPoint.Continue();
        await responseTask.DefaultTimeout();

        // Assert
        Assert.AreEqual("SECRET_TOKEN", authorizationValue);
    }

    [Test]
    public async Task CallCredentialsWithHttps_CancellationToken()
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var unreachableAuthInterceptorSection = false;
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.CancellationToken.Register(s => ((TaskCompletionSource<object?>)s!).SetCanceled(), tcs);

            await tcs.Task;

            unreachableAuthInterceptorSection = true;
        });
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        var responseTask = call.ResponseAsync;

        call.Dispose();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        // Assert
        Assert.False(unreachableAuthInterceptorSection);
    }

    [Test]
    public async Task CallCredentialsWithHttp_NoMetadataOnRequest()
    {
        // Arrange
        bool? hasAuthorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            hasAuthorizationValue = request.Headers.TryGetValues("authorization", out _);

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri("http://localhost"));

        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

        // Act
        var callCredentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            metadata.Add("authorization", "SECRET_TOKEN");
            return Task.CompletedTask;
        });
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(false, hasAuthorizationValue);

        var log = testSink.Writes.Single(w => w.EventId.Name == "CallCredentialsNotUsed");
        Assert.AreEqual("The configured CallCredentials were not used because the call does not use TLS.", log.State.ToString());
    }

    [Test]
    public async Task CallCredentialsWithHttp_UnsafeUseInsecureChannelCallCredentials_MetadataOnRequest()
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri("http://localhost"));
        var invoker = HttpClientCallInvokerFactory.Create(
            httpClient,
            configure: o => o.UnsafeUseInsecureChannelCallCredentials = true);

        // Act
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            // The operation is asynchronous to ensure delegate is awaited
            await Task.Delay(50);
            metadata.Add("authorization", "SECRET_TOKEN");
        });
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("SECRET_TOKEN", authorizationValue);
    }

    [Test]
    public async Task CallCredentialsWithHttp_UnsafeUseInsecureChannelCallCredentials_ChannelCredentials_MetadataOnRequest()
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri("http://localhost"));
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            // The operation is asynchronous to ensure delegate is awaited
            await Task.Delay(50);
            metadata.Add("authorization", "SECRET_TOKEN");
        });
        var invoker = HttpClientCallInvokerFactory.Create(
            httpClient,
            configure: o =>
            {
                o.UnsafeUseInsecureChannelCallCredentials = true;
                o.Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials);
            });

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("SECRET_TOKEN", authorizationValue);
    }

    [Test]
    public void CallCredentialsWithHttp_ChannelCredentials_Error()
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri("http://localhost"));
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            // The operation is asynchronous to ensure delegate is awaited
            await Task.Delay(50);
            metadata.Add("authorization", "SECRET_TOKEN");
        });

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = HttpClientCallInvokerFactory.Create(
                httpClient,
                configure: o =>
                {
                    o.Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials);
                });
        })!;

        // Assert
        Assert.AreEqual("CallCredentials can't be composed with InsecureCredentials. " +
            "CallCredentials must be used with secure channel credentials like SslCredentials " +
            "or by enabling GrpcChannelOptions.UnsafeUseInsecureChannelCallCredentials on the channel.", ex.Message);
    }

    [TestCase(true, "https://localhost")]
    [TestCase(false, "http://localhost")]
    public void CallCredentialsWithHttp_CustomChannelCredentials_CheckSecureStatus(bool isSecure, string address)
    {
        // Arrange
        string? authorizationValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            authorizationValue = request.Headers.GetValues("authorization").Single();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri(address));
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            // The operation is asynchronous to ensure delegate is awaited
            await Task.Delay(50);
            metadata.Add("authorization", "SECRET_TOKEN");
        });

        if (isSecure)
        {
            // Act && Assert
            CreateInvoker(httpClient, isSecure, callCredentials);
        }
        else
        {
            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => CreateInvoker(httpClient, isSecure, callCredentials))!;
            // Assert
            Assert.AreEqual("CallCredentials can't be composed with FakeChannelCredentials. " +
                "CallCredentials must be used with secure channel credentials like SslCredentials " +
                "or by enabling GrpcChannelOptions.UnsafeUseInsecureChannelCallCredentials on the channel.", ex.Message);
        }

        static Internal.HttpClientCallInvoker CreateInvoker(HttpClient httpClient, bool isSecure, CallCredentials callCredentials)
        {
            return HttpClientCallInvokerFactory.Create(
                httpClient,
                configure: o =>
                {
                    o.Credentials = ChannelCredentials.Create(new FakeChannelCredentials(isSecure), callCredentials);
                });
        }
    }

    internal class FakeChannelCredentials : ChannelCredentials
    {
        private readonly bool _isSecure;

        public FakeChannelCredentials(bool isSecure)
        {
            _isSecure = isSecure;
        }

        public override void InternalPopulateConfiguration(ChannelCredentialsConfiguratorBase configurator, object state)
        {
            if (_isSecure)
            {
                configurator.SetSslCredentials(this, null, null, null);
            }
            else
            {
                configurator.SetInsecureCredentials(this);
            }
        }
    }

    [Test]
    public async Task CompositeCallCredentialsWithHttps_MetadataOnRequest()
    {
        // Arrange
        HttpRequestHeaders? requestHeaders = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            requestHeaders = request.Headers;

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        var first = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) =>
        {
            metadata.Add("first_authorization", "FIRST_SECRET_TOKEN");
            return Task.CompletedTask;
        }));
        var second = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) =>
        {
            metadata.Add("second_authorization", "SECOND_SECRET_TOKEN");
            return Task.CompletedTask;
        }));
        var third = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) =>
        {
            metadata.Add("third_authorization", "THIRD_SECRET_TOKEN");
            return Task.CompletedTask;
        }));

        // Act
        var callCredentials = CallCredentials.Compose(first, second, third);
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("FIRST_SECRET_TOKEN", requestHeaders!.GetValues("first_authorization").Single());
        Assert.AreEqual("SECOND_SECRET_TOKEN", requestHeaders!.GetValues("second_authorization").Single());
        Assert.AreEqual("THIRD_SECRET_TOKEN", requestHeaders!.GetValues("third_authorization").Single());
    }

    [Test]
    [TestCase("https://somehost", "https://somehost/ServiceName")]
    [TestCase("https://somehost/", "https://somehost/ServiceName")]
    [TestCase("https://somehost:443", "https://somehost/ServiceName")]
    [TestCase("https://somehost:443/", "https://somehost/ServiceName")]
    [TestCase("https://somehost:1234", "https://somehost:1234/ServiceName")]
    [TestCase("https://foo.bar:443", "https://foo.bar/ServiceName")]
    [TestCase("https://foo.bar:443/abc/xyz", "https://foo.bar/abc/xyz/ServiceName")]
    public async Task CallCredentials_AuthContextPopulated(string target, string expectedServiceUrl)
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }, new Uri(target));
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        string? serviceUrl = null;
        string? methodName = null;
        var callCredentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            serviceUrl = context.ServiceUrl;
            methodName = context.MethodName;
            return Task.CompletedTask;
        });
        var call = invoker.AsyncUnaryCall(new HelloRequest(), new CallOptions(credentials: callCredentials));
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(expectedServiceUrl, serviceUrl);
        Assert.AreEqual("MethodName", ClientTestHelpers.ServiceMethod.Name);
    }
}
