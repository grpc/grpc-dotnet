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

using System.Diagnostics;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class TelemetryTests : FunctionalTestBase
{
    [TestCase(ClientType.Channel)]
    [TestCase(ClientType.ClientFactory)]
    public async Task InternalHandler_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
    {
        await TestTelemetryHeaderIsSet(clientType, handler: null);
    }

    [TestCase(ClientType.Channel)]
    [TestCase(ClientType.ClientFactory)]
    public async Task Channel_SocketsHttpHandler_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
    {
        await TestTelemetryHeaderIsSet(clientType, handler: new SocketsHttpHandler());
    }

    [TestCase(ClientType.Channel)]
    [TestCase(ClientType.ClientFactory)]
    public async Task Channel_SocketsHttpHandlerWrapped_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
    {
        await TestTelemetryHeaderIsSet(clientType, handler: new TestDelegatingHandler(new SocketsHttpHandler()));
    }

    private class TestDelegatingHandler : DelegatingHandler
    {
        public TestDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }
    }

    private async Task TestTelemetryHeaderIsSet(ClientType clientType, HttpMessageHandler? handler)
    {
        string? telemetryHeader = null;
        Task<HelloReply> UnaryTelemetryHeader(HelloRequest request, ServerCallContext context)
        {
            telemetryHeader = context.RequestHeaders.GetValue("traceparent");

            return Task.FromResult(new HelloReply());
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryTelemetryHeader);
        var client = CreateClient(clientType, method, handler);

        // Act
        var result = new List<KeyValuePair<string, object?>>();

        using var allSubscription = new AllListenersObserver(new Dictionary<string, IObserver<KeyValuePair<string, object?>>>
        {
            ["HttpHandlerDiagnosticListener"] = new ObserverToList<KeyValuePair<string, object?>>(result)
        });
        using (DiagnosticListener.AllListeners.Subscribe(allSubscription))
        {
            await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();
        }

        // Assert
        Assert.IsNotNull(telemetryHeader);
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual("System.Net.Http.HttpRequestOut.Start", result[0].Key);
        Assert.AreEqual("System.Net.Http.Request", result[1].Key);
        Assert.AreEqual("System.Net.Http.HttpRequestOut.Stop", result[2].Key);
        Assert.AreEqual("System.Net.Http.Response", result[3].Key);
    }

    private TestClient<HelloRequest, HelloReply> CreateClient(ClientType clientType, Method<HelloRequest, HelloReply> method, HttpMessageHandler? handler)
    {
        switch (clientType)
        {
            case ClientType.Channel:
                {
                    var options = new GrpcChannelOptions
                    {
                        LoggerFactory = LoggerFactory,
                        HttpHandler = handler
                    };

                    // Want to test the behavior of the default, internally created handler.
                    // Only supply the URL to a manually created GrpcChannel.
                    var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2), options);
                    return TestClientFactory.Create(channel, method);
                }
            case ClientType.ClientFactory:
                {
                    var serviceCollection = new ServiceCollection();
                    serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
                    serviceCollection
                        .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
                        {
                            options.Address = Fixture.GetUrl(TestServerEndpointName.Http2);
                        })
                        .ConfigureGrpcClientCreator(invoker =>
                        {
                            return TestClientFactory.Create(invoker, method);
                        });
                    var services = serviceCollection.BuildServiceProvider();

                    return services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();
                }
            default:
                throw new InvalidOperationException("Unexpected value.");
        }
    }

    public enum ClientType
    {
        Channel,
        ClientFactory
    }

    internal class AllListenersObserver : IObserver<DiagnosticListener>, IDisposable
    {
        private readonly Dictionary<string, IObserver<KeyValuePair<string, object?>>> _observers;
        private readonly List<IDisposable> _subscriptions;

        public AllListenersObserver(Dictionary<string, IObserver<KeyValuePair<string, object?>>> observers)
        {
            _observers = observers;
            _subscriptions = new List<IDisposable>();
        }

        public bool Completed { get; private set; }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        public void OnCompleted()
        {
            Completed = true;
        }

        public void OnError(Exception error)
        {
            throw new Exception("Observer error", error);
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value?.Name != null && _observers.TryGetValue(value.Name, out var observer))
            {
                _subscriptions.Add(value.Subscribe(observer));
            }
        }
    }

    internal class ObserverToList<T> : IObserver<T>
    {
        public ObserverToList(List<T> output, Predicate<T>? filter = null, string? name = null)
        {
            _output = output;
            _output.Clear();
            _filter = filter;
            _name = name;
        }

        public bool Completed { get; private set; }

        #region private
        public void OnCompleted()
        {
            Completed = true;
        }

        public void OnError(Exception error)
        {
            Assert.True(false, "Error happened on IObserver");
        }

        public void OnNext(T value)
        {
            Assert.False(Completed);
            if (_filter == null || _filter(value))
            {
                _output.Add(value);
            }
        }

        private readonly List<T> _output;
        private readonly Predicate<T>? _filter;
        private readonly string? _name;  // for debugging 
        #endregion
    }
}
