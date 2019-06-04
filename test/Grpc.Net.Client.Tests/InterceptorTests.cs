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
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Utils;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
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
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                if (request.Headers.TryGetValues(HeaderKey, out var values))
                {
                    requestHeaderValue = values.Single();
                }

                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
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

            var result = callInvoker.BlockingUnaryCall(TestHelpers.ServiceMethod, Host, new CallOptions(), new HelloRequest());

            // Assert
            Assert.AreEqual("PASS", result.Message);
            Assert.AreEqual(HeaderValue, requestHeaderValue);
        }

        [Test]
        public void Intercept_InterceptorOrder_ExecutedInReversedOrder()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var stringBuilder = new StringBuilder();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var callInvoker = invoker.Intercept(metadata =>
            {
                stringBuilder.Append("interceptor1");
                return metadata;
            }).Intercept(new CallbackInterceptor(() => stringBuilder.Append("array1")),
                new CallbackInterceptor(() => stringBuilder.Append("array2")),
                new CallbackInterceptor(() => stringBuilder.Append("array3")))
            .Intercept(metadata =>
            {
                stringBuilder.Append("interceptor2");
                return metadata;
            }).Intercept(metadata =>
            {
                stringBuilder.Append("interceptor3");
                return metadata;
            });

            var result = callInvoker.BlockingUnaryCall(TestHelpers.ServiceMethod, Host, new CallOptions(), new HelloRequest());

            // Assert
            Assert.AreEqual("PASS", result.Message);
            Assert.AreEqual("interceptor3interceptor2array1array2array3interceptor1", stringBuilder.ToString());
        }

        [Test]
        public async Task Intercept_WrapClientStream_ClientStreamWrapperExecuted()
        {
            // Arrange
            var serviceMethod = new Method<string, string>(MethodType.Unary, "ServiceName", "Unary", Marshallers.StringMarshaller, Marshallers.StringMarshaller);

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var requestContent = await request.Content.ReadAsStreamAsync();
                await requestContent.CopyToAsync(new MemoryStream());

                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply { Message = "PASS" }).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var stringBuilder = new StringBuilder();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var callInvoker = invoker.Intercept(new ClientStreamingCountingInterceptor());

            var call = callInvoker.AsyncClientStreamingCall(serviceMethod, Host, new CallOptions());
            await call.RequestStream.WriteAllAsync(new [] { "A", "B", "C" });

            // Assert
            Assert.AreEqual("3", await call.ResponseAsync);

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
            Assert.IsNotNull(call.GetTrailers());
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
                    unaryResponse => (TResponse)(object)counter.ToString()  // Cast to object first is needed to satisfy the type-checker    
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
                    return writer.CompleteAsync().ContinueWith(x => onResponseStreamEnd());
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
            public WriteOptions WriteOptions
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
}
