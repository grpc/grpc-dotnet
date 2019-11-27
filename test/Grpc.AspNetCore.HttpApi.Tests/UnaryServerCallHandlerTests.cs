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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server.HttpApi;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Shared.Server;
using Grpc.Tests.Shared;
using HttpApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HttpApi
{
    [TestFixture]
    public class UnaryServerCallHandlerTests
    {
        [Test]
        public async Task HandleCallAsync_MatchingRouteValue_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply { Message = $"Hello {r.Name}" });
            };

            var routeParameterDescriptors = new Dictionary<string, List<FieldDescriptor>>
            {
                ["name"] = new List<FieldDescriptor>(new[] { HelloRequest.Descriptor.FindFieldByNumber(HelloRequest.NameFieldNumber) }),
                ["sub.subfield"] = new List<FieldDescriptor>(new[]
                {
                    HelloRequest.Descriptor.FindFieldByNumber(HelloRequest.SubFieldNumber),
                    HelloRequest.Types.SubMessage.Descriptor.FindFieldByNumber(HelloRequest.Types.SubMessage.SubfieldFieldNumber)
                })
            };
            var unaryServerCallHandler = CreateCallHandler(invoker, routeParameterDescriptors: routeParameterDescriptors);
            var httpContext = CreateHttpContext();
            httpContext.Request.RouteValues["name"] = "TestName!";
            httpContext.Request.RouteValues["sub.subfield"] = "Subfield!";

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("TestName!", request!.Name);
            Assert.AreEqual("Subfield!", request!.Sub.Subfield);

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using var responseJson = JsonDocument.Parse(httpContext.Response.Body);
            Assert.AreEqual("Hello TestName!", responseJson.RootElement.GetProperty("message").GetString());
        }

        [Test]
        public async Task HandleCallAsync_ResponseBodySet_ResponseReturned()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply { Message = $"Hello {r.Name}" });
            };

            var routeParameterDescriptors = new Dictionary<string, List<FieldDescriptor>>
            {
                ["name"] = new List<FieldDescriptor>(new[] { HelloRequest.Descriptor.FindFieldByNumber(HelloRequest.NameFieldNumber) })
            };
            var unaryServerCallHandler = CreateCallHandler(
                invoker,
                HelloReply.Descriptor.FindFieldByNumber(HelloReply.MessageFieldNumber),
                routeParameterDescriptors);
            var httpContext = CreateHttpContext();
            httpContext.Request.RouteValues["name"] = "TestName!";

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("TestName!", request!.Name);

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using var responseJson = JsonDocument.Parse(httpContext.Response.Body);
            Assert.AreEqual("Hello TestName!", responseJson.RootElement.GetString());
        }

        [Test]
        public async Task HandleCallAsync_ResponseBodySetToRepeatedField_ArrayReturned()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply { Values = { "One", "Two", "Three" } });
            };

            var unaryServerCallHandler = CreateCallHandler(
                invoker,
                HelloReply.Descriptor.FindFieldByNumber(HelloReply.ValuesFieldNumber));
            var httpContext = CreateHttpContext();

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using var responseJson = JsonDocument.Parse(httpContext.Response.Body);
            Assert.AreEqual(JsonValueKind.Array, responseJson.RootElement.ValueKind);
            Assert.AreEqual("One", responseJson.RootElement[0].GetString());
            Assert.AreEqual("Two", responseJson.RootElement[1].GetString());
            Assert.AreEqual("Three", responseJson.RootElement[2].GetString());
        }

        [Test]
        public async Task HandleCallAsync_RootBodySet_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply { Message = $"Hello {r.Name}" });
            };

            var unaryServerCallHandler = CreateCallHandler(
                invoker,
                bodyDescriptor: HelloRequest.Descriptor);
            var httpContext = CreateHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(new HelloRequest
            {
                Name = "TestName!"
            })));
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["name"] = "QueryStringTestName!",
                ["sub.subfield"] = "QueryStringTestSubfield!"
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("TestName!", request!.Name);
            Assert.AreEqual(null, request!.Sub);
        }

        [Test]
        public async Task HandleCallAsync_SubBodySet_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply { Message = $"Hello {r.Name}" });
            };

            var unaryServerCallHandler = CreateCallHandler(
                invoker,
                bodyDescriptor: HelloRequest.Types.SubMessage.Descriptor,
                bodyFieldDescriptor: HelloRequest.Descriptor.FindFieldByName("sub"));
            var httpContext = CreateHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(new HelloRequest.Types.SubMessage
            {
                Subfield = "Subfield!"
            })));
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["name"] = "QueryStringTestName!",
                ["sub.subfield"] = "QueryStringTestSubfield!",
                ["sub.subfields"] = "QueryStringTestSubfields!"
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("QueryStringTestName!", request!.Name);
            Assert.AreEqual("Subfield!", request!.Sub.Subfield);
            Assert.AreEqual(0, request!.Sub.Subfields.Count);
        }

        [Test]
        public async Task HandleCallAsync_MatchingQueryStringValues_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply());
            };

            var unaryServerCallHandler = CreateCallHandler(invoker);
            var httpContext = CreateHttpContext();
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["name"] = "TestName!",
                ["sub.subfield"] = "TestSubfield!"
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("TestName!", request!.Name);
            Assert.AreEqual("TestSubfield!", request!.Sub.Subfield);
        }

        [Test]
        public async Task HandleCallAsync_MatchingRepeatedQueryStringValues_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply());
            };

            var unaryServerCallHandler = CreateCallHandler(invoker);
            var httpContext = CreateHttpContext();
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["sub.subfields"] = new StringValues(new[] { "TestSubfields1!", "TestSubfields2!" })
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual(2, request!.Sub.Subfields.Count);
            Assert.AreEqual("TestSubfields1!", request!.Sub.Subfields[0]);
            Assert.AreEqual("TestSubfields2!", request!.Sub.Subfields[1]);
        }

        [Test]
        public async Task HandleCallAsync_DataTypes_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply());
            };

            var unaryServerCallHandler = CreateCallHandler(invoker);
            var httpContext = CreateHttpContext();
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["data.single_int32"] = "1",
                ["data.single_int64"] = "2",
                ["data.single_uint32"] = "3",
                ["data.single_uint64"] = "4",
                ["data.single_sint32"] = "5",
                ["data.single_sint64"] = "6",
                ["data.single_fixed32"] = "7",
                ["data.single_fixed64"] = "8",
                ["data.single_sfixed32"] = "9",
                ["data.single_sfixed64"] = "10",
                ["data.single_float"] = "11.1",
                ["data.single_double"] = "12.1",
                ["data.single_bool"] = "true",
                ["data.single_string"] = "A string",
                ["data.single_bytes"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                ["data.single_enum"] = "FOO"
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual(1, request!.Data.SingleInt32);
            Assert.AreEqual(2, request!.Data.SingleInt64);
            Assert.AreEqual(3, request!.Data.SingleUint32);
            Assert.AreEqual(4, request!.Data.SingleUint64);
            Assert.AreEqual(5, request!.Data.SingleSint32);
            Assert.AreEqual(6, request!.Data.SingleSint64);
            Assert.AreEqual(7, request!.Data.SingleFixed32);
            Assert.AreEqual(8, request!.Data.SingleFixed64);
            Assert.AreEqual(9, request!.Data.SingleSfixed32);
            Assert.AreEqual(10, request!.Data.SingleSfixed64);
            Assert.AreEqual(11.1, request!.Data.SingleFloat, 0.001);
            Assert.AreEqual(12.1, request!.Data.SingleDouble, 0.001);
            Assert.AreEqual(true, request!.Data.SingleBool);
            Assert.AreEqual("A string", request!.Data.SingleString);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, request!.Data.SingleBytes.ToByteArray());
            Assert.AreEqual(HelloRequest.Types.DataTypes.Types.NestedEnum.Foo, request!.Data.SingleEnum);
        }

        [Test]
        public async Task HandleCallAsync_Wrappers_SetOnRequestMessage()
        {
            // Arrange
            HelloRequest? request = null;
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker = (s, r, c) =>
            {
                request = r;
                return Task.FromResult(new HelloReply());
            };

            var unaryServerCallHandler = CreateCallHandler(invoker);
            var httpContext = CreateHttpContext();
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["wrappers.string_value"] = "1",
                ["wrappers.int32_value"] = "2",
                ["wrappers.int64_value"] = "3",
                ["wrappers.float_value"] = "4.1",
                ["wrappers.double_value"] = "5.1",
                ["wrappers.bool_value"] = "true",
                ["wrappers.uint32_value"] = "7",
                ["wrappers.uint64_value"] = "8",
                ["wrappers.bytes_value"] = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            });

            // Act
            await unaryServerCallHandler.HandleCallAsync(httpContext);

            // Assert
            Assert.IsNotNull(request);
            Assert.AreEqual("1", request!.Wrappers.StringValue);
            Assert.AreEqual(2, request!.Wrappers.Int32Value);
            Assert.AreEqual(3, request!.Wrappers.Int64Value);
            Assert.AreEqual(4.1, request!.Wrappers.FloatValue, 0.001);
            Assert.AreEqual(5.1, request!.Wrappers.DoubleValue, 0.001);
            Assert.AreEqual(true, request!.Wrappers.BoolValue);
            Assert.AreEqual(7, request!.Wrappers.Uint32Value);
            Assert.AreEqual(8, request!.Wrappers.Uint64Value);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, request!.Wrappers.BytesValue.ToByteArray());
        }

        private static DefaultHttpContext CreateHttpContext()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<HttpApiGreeterService>();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = serviceProvider;
            httpContext.Response.Body = new MemoryStream();
            return httpContext;
        }

        private static UnaryServerCallHandler<HttpApiGreeterService, HelloRequest, HelloReply> CreateCallHandler(
            UnaryServerMethod<HttpApiGreeterService, HelloRequest, HelloReply> invoker,
            FieldDescriptor? responseBodyDescriptor = null,
            Dictionary<string, List<FieldDescriptor>>? routeParameterDescriptors = null,
            MessageDescriptor? bodyDescriptor = null,
            FieldDescriptor? bodyFieldDescriptor = null)
        {
            var unaryServerCallInvoker = new UnaryServerMethodInvoker<HttpApiGreeterService, HelloRequest, HelloReply>(
                invoker,
                CreateServiceMethod<HelloRequest, HelloReply>("TestMethodName", HelloRequest.Parser, HelloReply.Parser),
                MethodOptions.Create(new[] { new GrpcServiceOptions() }),
                new TestGrpcServiceActivator<HttpApiGreeterService>());
            
            return new UnaryServerCallHandler<HttpApiGreeterService, HelloRequest, HelloReply>(
                unaryServerCallInvoker,
                responseBodyDescriptor,
                bodyDescriptor,
                bodyFieldDescriptor,
                routeParameterDescriptors ?? new Dictionary<string, List<FieldDescriptor>>());
        }

        private class HttpApiGreeterService : HttpApiGreeter.HttpApiGreeterBase
        {
            public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
            {
                return base.SayHello(request, context);
            }
        }

        public static Marshaller<TMessage> GetMarshaller<TMessage>(MessageParser<TMessage> parser) where TMessage : IMessage<TMessage> =>
            Marshallers.Create<TMessage>(r => r.ToByteArray(), data => parser.ParseFrom(data));

        public static readonly Method<HelloRequest, HelloReply> ServiceMethod = CreateServiceMethod("MethodName", HelloRequest.Parser, HelloReply.Parser);

        public static Method<TRequest, TResponse> CreateServiceMethod<TRequest, TResponse>(string methodName, MessageParser<TRequest> requestParser, MessageParser<TResponse> responseParser)
             where TRequest : IMessage<TRequest>
             where TResponse : IMessage<TResponse>
        {
            return new Method<TRequest, TResponse>(MethodType.Unary, "ServiceName", methodName, GetMarshaller(requestParser), GetMarshaller(responseParser));
        }

    }
}
