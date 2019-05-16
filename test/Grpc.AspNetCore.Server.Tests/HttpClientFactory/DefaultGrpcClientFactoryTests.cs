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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.GrpcClient;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HttpClientFactory
{
    [TestFixture]
    public class DefaultGrpcClientFactoryTests
    {
        [Test]
        public void CreateClient_MultipleNamedClients_ReturnMatchingClient()
        {
            // Arrange
            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            services.AddGrpcClient<TestGreeterClient>("contoso", options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            });
            services.AddGrpcClient<TestGreeterClient>("adventureworks", options =>
            {
                options.BaseAddress = new Uri("http://adventureworks");
            });

            var provider = services.BuildServiceProvider();

            // Act
            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            var contosoClient = clientFactory.CreateClient<TestGreeterClient>("contoso");
            var adventureworksClient = clientFactory.CreateClient<TestGreeterClient>("adventureworks");

            // Assert
            Assert.AreEqual("http://contoso", contosoClient.GetCallInvoker().BaseAddress.OriginalString);
            Assert.AreEqual("http://adventureworks", adventureworksClient.GetCallInvoker().BaseAddress.OriginalString);
        }

        [Test]
        public void CreateClient_UnmatchedName_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            });

            var provider = services.BuildServiceProvider();

            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<TestGreeterClient>("DOES_NOT_EXIST"));

            // Assert
            Assert.AreEqual("No gRPC client configured with name 'DOES_NOT_EXIST'.", ex.Message);
        }

        [Test]
        public async Task CreateClient_LoggingSetup_ClientLogsToTestSink()
        {
            // Arrange
            var testSink = new TestSink();

            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            var clientBuilder = services.AddGrpcClient<TestGreeterClient>("contoso", options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            }).AddHttpMessageHandler(() => new TestDelegatingHandler());
            services.AddLogging(configure => configure.SetMinimumLevel(LogLevel.Trace));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TestLoggerProvider>(s => new TestLoggerProvider(testSink, true)));

            var provider = services.BuildServiceProvider();

            // Act
            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            var contosoClient = clientFactory.CreateClient<TestGreeterClient>("contoso");

            var response = await contosoClient.SayHelloAsync(new HelloRequest());

            // Assert
            Assert.AreEqual("http://contoso", contosoClient.GetCallInvoker().BaseAddress.OriginalString);

            Assert.IsTrue(testSink.Writes.Any(w => w.EventId.Name == "StartingCall"));
        }
    }

    public class TestDelegatingHandler : DelegatingHandler
    {
        public TestDelegatingHandler()
        {
        }

        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }
    }

    public class TestLoggerProvider : ILoggerProvider
    {
        private readonly Func<LogLevel, bool> _filter;

        public TestLoggerProvider(TestSink testSink, bool isEnabled) :
            this(testSink, _ => isEnabled)
        {
        }

        public TestLoggerProvider(TestSink testSink, Func<LogLevel, bool> filter)
        {
            Sink = testSink;
            _filter = filter;
        }

        public TestSink Sink { get; }

        public bool DisposeCalled { get; private set; }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(categoryName, Sink, _filter);
        }

        public void Dispose()
        {
            DisposeCalled = true;
        }
    }
}
