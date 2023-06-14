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
using System.Net.Http;
using System.Net.Http.Headers;
using Greet;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class AsyncUnaryCallTests
{
    [Test]
    public async Task AsyncUnaryCall_Success_HttpRequestMessagePopulated()
    {
        // Arrange
        HttpRequestMessage? httpRequestMessage = null;
        long? requestContentLength = null;

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            httpRequestMessage = request;
            requestContentLength = httpRequestMessage!.Content!.Headers!.ContentLength;

            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

        // Assert
        Assert.AreEqual("Hello world", rs.Message);

        Assert.IsNotNull(httpRequestMessage);
        Assert.AreEqual(new Version(2, 0), httpRequestMessage!.Version);
        Assert.AreEqual(HttpMethod.Post, httpRequestMessage.Method);
        Assert.AreEqual(new Uri("https://localhost/ServiceName/MethodName"), httpRequestMessage.RequestUri);
        Assert.AreEqual(new MediaTypeHeaderValue("application/grpc"), httpRequestMessage.Content?.Headers?.ContentType);
        Assert.AreEqual(GrpcProtocolConstants.TEHeaderValue, httpRequestMessage.Headers.TE.Single().Value);
#if NET6_0_OR_GREATER
        Assert.AreEqual("identity,gzip,deflate", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());
#else
        Assert.AreEqual("identity,gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());
#endif
        Assert.AreEqual(null, requestContentLength);

        var grpcVersion = httpRequestMessage.Headers.UserAgent.First();
        Assert.AreEqual("grpc-dotnet", grpcVersion.Product?.Name);
        Assert.IsTrue(!string.IsNullOrEmpty(grpcVersion.Product?.Version));

        // Sanity check that the user agent doesn't have the git hash in it.
        Assert.IsFalse(grpcVersion.Product!.Version!.Contains('+'));
    }

    [Test]
    public async Task AsyncUnaryCall_HasWinHttpHandler_ContentLengthOnHttpRequestMessagePopulated()
    {
        // Arrange
        HttpRequestMessage? httpRequestMessage = null;
        long? requestContentLength = null;

        var handler = TestHttpMessageHandler.Create(async request =>
        {
            httpRequestMessage = request;
            requestContentLength = httpRequestMessage!.Content!.Headers!.ContentLength;

            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

#pragma warning disable CS0436 // Just need to have a type called WinHttpHandler to activate new behavior.
        var winHttpHandler = new WinHttpHandler(handler);
#pragma warning restore CS0436
        var invoker = HttpClientCallInvokerFactory.Create(winHttpHandler, "https://localhost");

        // Act
        var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(
            ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "Hello world" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Hello world", rs.Message);

        Assert.IsNotNull(httpRequestMessage);
        Assert.AreEqual(18, requestContentLength);
    }

    [Test]
    public async Task AsyncUnaryCall_Success_RequestContentSent()
    {
        // Arrange
        HttpContent? content = null;

        var handler = TestHttpMessageHandler.Create(async request =>
        {
            content = request.Content;

            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(handler, "http://localhost");

        // Act
        var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(
            ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Hello world", rs.Message);

        Assert.IsNotNull(content);

        var requestContent = await content!.ReadAsStreamAsync().DefaultTimeout();
        var requestMessage = await StreamSerializationHelper.ReadMessageAsync(
            requestContent,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            GrpcProtocolConstants.IdentityGrpcEncoding,
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: true,
            CancellationToken.None).DefaultTimeout();

        Assert.AreEqual("World", requestMessage!.Name);
    }

    [Test]
    public async Task AsyncUnaryCall_NonOkStatusTrailer_AccessResponse_ThrowRpcError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()), StatusCode.Unimplemented);
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Test]
    public async Task AsyncUnaryCall_NonOkStatusTrailer_AccessHeaders_ReturnHeaders()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unimplemented, customHeaders: new Dictionary<string, string> { ["custom"] = "true" });
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var headers = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("true", headers.GetValue("custom"));
    }

    [Test]
    public async Task AsyncUnaryCall_SuccessTrailersOnly_ThrowNoMessageError()
    {
        // Arrange
        HttpResponseMessage? responseMessage = null;
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            responseMessage = ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.OK, customHeaders: new Dictionary<string, string> { [GrpcProtocolConstants.MessageTrailer] = "Detail!" });
            return Task.FromResult(responseMessage);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
        var headers = await call.ResponseHeadersAsync.DefaultTimeout();
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.NotNull(responseMessage);

        Assert.IsFalse(responseMessage!.TrailingHeaders().Any()); // sanity check that there are no trailers

        Assert.AreEqual(StatusCode.Internal, ex.Status.StatusCode);
        StringAssert.StartsWith("Failed to deserialize response message.", ex.Status.Detail);

        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
        StringAssert.StartsWith("Failed to deserialize response message.", call.GetStatus().Detail);

        Assert.AreEqual(0, headers.Count);
        Assert.AreEqual(0, call.GetTrailers().Count);
    }

    public enum ResponseHandleAction
    {
        ResponseAsync,
        ResponseHeadersAsync,
        Dispose,
        Nothing
    }

    [Test]
    [TestCase(0, false, ResponseHandleAction.ResponseAsync)]
    [TestCase(0, true, ResponseHandleAction.ResponseAsync)]
    [TestCase(0, false, ResponseHandleAction.ResponseHeadersAsync)]
    [TestCase(0, false, ResponseHandleAction.Dispose)]
    [TestCase(1, false, ResponseHandleAction.Nothing)]
    public async Task AsyncUnaryCall_CallFailed_NoUnobservedExceptions(int expectedUnobservedExceptions, bool addClientInterceptor, ResponseHandleAction action)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<CancellationTests>();

        var unobservedExceptions = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobservedTaskException = (sender, e) =>
        {
            unobservedExceptions.Add(e.Exception!);

            logger.LogCritical(e.Exception!, "Unobserved task exception. Observed: " + e.Observed);
        };

        TaskScheduler.UnobservedTaskException += onUnobservedTaskException;

        try
        {
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                throw new Exception("Test error");
            });
            CallInvoker invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: loggerFactory);
            if (addClientInterceptor)
            {
                invoker = invoker.Intercept(new ClientLoggerInterceptor(loggerFactory));
            }

            // Act
            logger.LogDebug("Starting call");
            await MakeGrpcCallAsync(logger, invoker, action);

            logger.LogDebug("Waiting for finalizers");
            // Provoke the garbage collector to find the unobserved exception.
            GC.Collect();
            // Wait for any failed tasks to be garbage collected
            GC.WaitForPendingFinalizers();

            // Assert
            Assert.AreEqual(expectedUnobservedExceptions, unobservedExceptions.Count);

            static async Task MakeGrpcCallAsync(ILogger logger, CallInvoker invoker, ResponseHandleAction action)
            {
                var runTask = Task.Run(async () =>
                {
                    var call = invoker.AsyncUnaryCall(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

                    switch (action)
                    {
                        case ResponseHandleAction.ResponseAsync:
                            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
                            break;
                        case ResponseHandleAction.ResponseHeadersAsync:
                            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync);
                            break;
                        case ResponseHandleAction.Dispose:
                            call.Dispose();
                            break;
                        default:
                            // Do nothing.
                            break;
                    }
                });

                await runTask;
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobservedTaskException;
        }
    }

    private class ClientLoggerInterceptor : Interceptor
    {
        private readonly ILogger<ClientLoggerInterceptor> _logger;

        public ClientLoggerInterceptor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ClientLoggerInterceptor>();
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            LogCall(context.Method);
            AddCallerMetadata(ref context);

            try
            {
                return continuation(request, context);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            LogCall(context.Method);
            AddCallerMetadata(ref context);

            try
            {
                var call = continuation(request, context);

                return new AsyncUnaryCall<TResponse>(HandleResponse(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        private async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> t)
        {
            try
            {
                var response = await t;
                _logger.LogInformation($"Response received: {response}");
                return response;
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            LogCall(context.Method);
            AddCallerMetadata(ref context);

            try
            {
                return continuation(context);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            LogCall(context.Method);
            AddCallerMetadata(ref context);

            try
            {
                return continuation(request, context);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            LogCall(context.Method);
            AddCallerMetadata(ref context);

            try
            {
                return continuation(context);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        private void LogCall<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            _logger.LogInformation($"Starting call. Name: {method.Name}. Type: {method.Type}. Request: {typeof(TRequest)}. Response: {typeof(TResponse)}");
        }

        private void AddCallerMetadata<TRequest, TResponse>(ref ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = context.Options.Headers;

            // Call doesn't have a headers collection to add to.
            // Need to create a new context with headers for the call.
            if (headers == null)
            {
                headers = new Metadata();
                var options = context.Options.WithHeaders(headers);
                context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
            }

            // Add caller metadata to call headers
            headers.Add("caller-user", Environment.UserName);
            headers.Add("caller-machine", Environment.MachineName);
            headers.Add("caller-os", Environment.OSVersion.ToString());
        }

        private void LogError(Exception ex)
        {
            _logger.LogError(ex, $"Call error: {ex.Message}");
        }
    }
}
