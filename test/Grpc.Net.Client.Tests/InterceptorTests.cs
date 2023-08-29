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

using System.Globalization;
using System.Net;
using System.Text;
using Greet;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

public class ClientInterceptorTest
{
    const string Host = "127.0.0.1";

    [Test]
    public void InterceptMetadata_AddRequestHeader_HeaderInRequest()
    {
        // Arrange
        const string HeaderKey = "x-client-interceptor";
        const string HeaderValue = "hello-world";

        string? requestHeaderValue = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            if (request.Headers.TryGetValues(HeaderKey, out var values))
            {
                requestHeaderValue = values.Single();
            }

            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var callInvoker = invoker.Intercept(metadata =>
        {
            metadata = metadata ?? new Metadata();
            metadata.Add(new Metadata.Entry(HeaderKey, HeaderValue));
            return metadata;
        });

        var result = callInvoker.BlockingUnaryCall(ClientTestHelpers.ServiceMethod, Host, new CallOptions(), new HelloRequest());

        // Assert
        Assert.AreEqual("PASS", result.Message);
        Assert.AreEqual(HeaderValue, requestHeaderValue);
    }

    [Test]
    public void Intercept_InterceptorOrder_ExecutedInReversedOrder()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var stringBuilder = new StringBuilder();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var callInvoker = invoker
            .Intercept(metadata =>
            {
                stringBuilder.Append("interceptor1");
                return metadata;
            })
            .Intercept(
                new CallbackInterceptor(o => stringBuilder.Append("array1")),
                new CallbackInterceptor(o => stringBuilder.Append("array2")),
                new CallbackInterceptor(o => stringBuilder.Append("array3")))
            .Intercept(metadata =>
            {
                stringBuilder.Append("interceptor2");
                return metadata;
            })
            .Intercept(metadata =>
            {
                stringBuilder.Append("interceptor3");
                return metadata;
            });

        var result = callInvoker.BlockingUnaryCall(ClientTestHelpers.ServiceMethod, Host, new CallOptions(), new HelloRequest());

        // Assert
        Assert.AreEqual("PASS", result.Message);
        Assert.AreEqual("interceptor3interceptor2array1array2array3interceptor1", stringBuilder.ToString());
    }

    [Test]
    public async Task Intercept_WrapClientStream_ClientStreamWrapperExecuted()
    {
        // Arrange
        var serviceMethod = new Method<string, string>(MethodType.ClientStreaming, "ServiceName", "Unary", Marshallers.StringMarshaller, Marshallers.StringMarshaller);

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var requestContent = await request.Content!.ReadAsStreamAsync().DefaultTimeout();
            await requestContent.CopyToAsync(new MemoryStream()).DefaultTimeout();

            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var stringBuilder = new StringBuilder();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var callInvoker = invoker.Intercept(new ClientStreamingCountingInterceptor());

        var call = callInvoker.AsyncClientStreamingCall(serviceMethod, Host, new CallOptions());
        await call.RequestStream.WriteAsync("A");
        await call.RequestStream.WriteAsync("B");
        await call.RequestStream.WriteAsync("C");
        await call.RequestStream.CompleteAsync().DefaultTimeout();

        // Assert
        Assert.AreEqual("3", await call.ResponseAsync);

        Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        Assert.IsNotNull(call.GetTrailers());
    }

    [TestCase(StatusCode.OK)]
    [TestCase(StatusCode.Internal)]
    public async Task Intercept_OnCompleted_StatusReturned(StatusCode statusCode)
    {
        await StatusReturnedCore(new OnCompletedInterceptor(), statusCode);
    }

    [TestCase(StatusCode.OK)]
    [TestCase(StatusCode.Internal)]
    public async Task Intercept_Await_StatusReturned(StatusCode statusCode)
    {
        await StatusReturnedCore(new AwaitInterceptor(), statusCode);
    }

    private static async Task StatusReturnedCore(StatusInterceptor interceptor, StatusCode statusCode)
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            if (statusCode == StatusCode.OK)
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            }
            else
            {
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, statusCode);
            }
        });

        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var callInvoker = invoker.Intercept(interceptor);

        var call = callInvoker.AsyncUnaryCall(ClientTestHelpers.ServiceMethod, Host, new CallOptions(), new HelloRequest());

        // Assert
        if (statusCode == StatusCode.OK)
        {
            var result = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("PASS", result.Message);
        }
        else
        {
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(statusCode, ex.StatusCode);
        }

        var interceptorStatusCode = await interceptor.GetStatusCodeAsync().DefaultTimeout();
        Assert.AreEqual(statusCode, interceptorStatusCode);
    }

    private abstract class StatusInterceptor : Interceptor
    {
        protected readonly TaskCompletionSource<StatusCode> StatusTcs = new TaskCompletionSource<StatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StatusCode> GetStatusCodeAsync() => StatusTcs.Task;
    }

    private class OnCompletedInterceptor : StatusInterceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var result = continuation(request, context);
            result.GetAwaiter().OnCompleted(() =>
            {
                try
                {
                    StatusTcs.SetResult(result.GetStatus().StatusCode);
                }
                catch (Exception ex)
                {
                    StatusTcs.SetException(ex);
                }
            });
            return result;
        }
    }

    private class AwaitInterceptor : StatusInterceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, context);

            return new AsyncUnaryCall<TResponse>(HandleResponse(call), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);

            async Task<TResponse> HandleResponse(AsyncUnaryCall<TResponse> call)
            {
                try
                {
                    return await call.ResponseAsync;
                }
                finally
                {
                    try
                    {
                        StatusTcs.SetResult(call.GetStatus().StatusCode);
                    }
                    catch (Exception ex)
                    {
                        StatusTcs.SetException(ex);
                    }
                }
            }
        }
    }

    private class ClientStreamingCountingInterceptor : Interceptor
    {
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var response = continuation(context);
            int counter = 0;
            var requestStream = new WrappedClientStreamWriter<TRequest>(response.RequestStream,
                message => { counter++; return message; }, null);
            var responseAsync = response.ResponseAsync.ContinueWith(
                unaryResponse => (TResponse)(object)counter.ToString(CultureInfo.InvariantCulture),  // Cast to object first is needed to satisfy the type-checker,
                TaskScheduler.Default
            );
            return new AsyncClientStreamingCall<TRequest, TResponse>(requestStream, responseAsync, response.ResponseHeadersAsync, response.GetStatus, response.GetTrailers, response.Dispose);
        }
    }

    private class WrappedClientStreamWriter<T> : IClientStreamWriter<T>
    {
        readonly IClientStreamWriter<T> writer;
        readonly Func<T, T> onMessage;
        readonly Action? onResponseStreamEnd;
        public WrappedClientStreamWriter(IClientStreamWriter<T> writer, Func<T, T> onMessage, Action? onResponseStreamEnd)
        {
            this.writer = writer;
            this.onMessage = onMessage;
            this.onResponseStreamEnd = onResponseStreamEnd;
        }
        public Task CompleteAsync()
        {
            if (onResponseStreamEnd != null)
            {
                return writer.CompleteAsync().ContinueWith(x => onResponseStreamEnd(), TaskScheduler.Default);
            }
            return writer.CompleteAsync();
        }
        public Task WriteAsync(T message)
        {
            if (onMessage != null)
            {
                message = onMessage(message);
            }
            return writer.WriteAsync(message);
        }
        public WriteOptions? WriteOptions
        {
            get
            {
                return writer.WriteOptions;
            }
            set
            {
                writer.WriteOptions = value;
            }
        }
    }
}
