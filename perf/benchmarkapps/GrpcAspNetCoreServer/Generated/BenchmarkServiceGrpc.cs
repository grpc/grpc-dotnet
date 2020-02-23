// <auto-generated>
//     Generated by the protocol buffer compiler.  DO NOT EDIT!
//     source: benchmark_service.proto
// </auto-generated>
// Original file comments:
// Copyright 2015 gRPC authors.
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
//
// An integration test service that covers all the method signature permutations
// of unary/streaming requests/responses.
#pragma warning disable 0414, 1591
#region Designer generated code

using grpc = global::Grpc.Core;

namespace Grpc.Testing
{
    public static partial class BenchmarkService
    {
        static readonly string __ServiceName = "grpc.testing.BenchmarkService";

        static void __Helper_SerializeMessage(global::Google.Protobuf.IMessage message, grpc::SerializationContext context)
        {
#if !GOOGLE_PROTOBUF_DISABLE_BUFFER_SERIALIZATION
            var bufferMessage = message as global::Google.Protobuf.IBufferMessage;
            if (bufferMessage != null)
            {
                context.SetPayloadLength(bufferMessage.CalculateSize());
                var writer = new global::Google.Protobuf.CodedOutputWriter(context.GetBufferWriter());
                bufferMessage.WriteTo(ref writer);
                writer.Flush();
                context.Complete();
                return;
            }
#endif
            context.Complete(global::Google.Protobuf.MessageExtensions.ToByteArray(message));
        }

        static T __Helper_DeserializeMessage<T>(grpc::DeserializationContext context, global::Google.Protobuf.MessageParser<T> parser) where T : global::Google.Protobuf.IMessage<T>
        {
#if !GOOGLE_PROTOBUF_DISABLE_BUFFER_SERIALIZATION
            return parser.ParseFrom(context.PayloadAsReadOnlySequence());
#else
            return parser.ParseFrom(context.PayloadAsNewBuffer());
#endif
        }

        static readonly grpc::Marshaller<global::Grpc.Testing.SimpleRequest> __Marshaller_grpc_testing_SimpleRequest = grpc::Marshallers.Create(__Helper_SerializeMessage, context => __Helper_DeserializeMessage(context, global::Grpc.Testing.SimpleRequest.Parser));
        static readonly grpc::Marshaller<global::Grpc.Testing.SimpleResponse> __Marshaller_grpc_testing_SimpleResponse = grpc::Marshallers.Create(__Helper_SerializeMessage, context => __Helper_DeserializeMessage(context, global::Grpc.Testing.SimpleResponse.Parser));

        static readonly grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse> __Method_UnaryCall = new grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(
            grpc::MethodType.Unary,
            __ServiceName,
            "UnaryCall",
            __Marshaller_grpc_testing_SimpleRequest,
            __Marshaller_grpc_testing_SimpleResponse);

        static readonly grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse> __Method_StreamingCall = new grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(
            grpc::MethodType.DuplexStreaming,
            __ServiceName,
            "StreamingCall",
            __Marshaller_grpc_testing_SimpleRequest,
            __Marshaller_grpc_testing_SimpleResponse);

        static readonly grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse> __Method_StreamingFromClient = new grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(
            grpc::MethodType.ClientStreaming,
            __ServiceName,
            "StreamingFromClient",
            __Marshaller_grpc_testing_SimpleRequest,
            __Marshaller_grpc_testing_SimpleResponse);

        static readonly grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse> __Method_StreamingFromServer = new grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(
            grpc::MethodType.ServerStreaming,
            __ServiceName,
            "StreamingFromServer",
            __Marshaller_grpc_testing_SimpleRequest,
            __Marshaller_grpc_testing_SimpleResponse);

        static readonly grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse> __Method_StreamingBothWays = new grpc::Method<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(
            grpc::MethodType.DuplexStreaming,
            __ServiceName,
            "StreamingBothWays",
            __Marshaller_grpc_testing_SimpleRequest,
            __Marshaller_grpc_testing_SimpleResponse);

        /// <summary>Service descriptor</summary>
        public static global::Google.Protobuf.Reflection.ServiceDescriptor Descriptor
        {
            get { return global::Grpc.Testing.BenchmarkServiceReflection.Descriptor.Services[0]; }
        }

        /// <summary>Base class for server-side implementations of BenchmarkService</summary>
        [grpc::BindServiceMethod(typeof(BenchmarkService), "BindService")]
        public abstract partial class BenchmarkServiceBase
        {
            /// <summary>
            /// One request followed by one response.
            /// The server returns the client payload as-is.
            /// </summary>
            /// <param name="request">The request received from the client.</param>
            /// <param name="context">The context of the server-side call handler being invoked.</param>
            /// <returns>The response to send back to the client (wrapped by a task).</returns>
            public virtual global::System.Threading.Tasks.Task<global::Grpc.Testing.SimpleResponse> UnaryCall(global::Grpc.Testing.SimpleRequest request, grpc::ServerCallContext context)
            {
                throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
            }

            /// <summary>
            /// Repeated sequence of one request followed by one response.
            /// Should be called streaming ping-pong
            /// The server returns the client payload as-is on each response
            /// </summary>
            /// <param name="requestStream">Used for reading requests from the client.</param>
            /// <param name="responseStream">Used for sending responses back to the client.</param>
            /// <param name="context">The context of the server-side call handler being invoked.</param>
            /// <returns>A task indicating completion of the handler.</returns>
            public virtual global::System.Threading.Tasks.Task StreamingCall(grpc::IAsyncStreamReader<global::Grpc.Testing.SimpleRequest> requestStream, grpc::IServerStreamWriter<global::Grpc.Testing.SimpleResponse> responseStream, grpc::ServerCallContext context)
            {
                throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
            }

            /// <summary>
            /// Single-sided unbounded streaming from client to server
            /// The server returns the client payload as-is once the client does WritesDone
            /// </summary>
            /// <param name="requestStream">Used for reading requests from the client.</param>
            /// <param name="context">The context of the server-side call handler being invoked.</param>
            /// <returns>The response to send back to the client (wrapped by a task).</returns>
            public virtual global::System.Threading.Tasks.Task<global::Grpc.Testing.SimpleResponse> StreamingFromClient(grpc::IAsyncStreamReader<global::Grpc.Testing.SimpleRequest> requestStream, grpc::ServerCallContext context)
            {
                throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
            }

            /// <summary>
            /// Single-sided unbounded streaming from server to client
            /// The server repeatedly returns the client payload as-is
            /// </summary>
            /// <param name="request">The request received from the client.</param>
            /// <param name="responseStream">Used for sending responses back to the client.</param>
            /// <param name="context">The context of the server-side call handler being invoked.</param>
            /// <returns>A task indicating completion of the handler.</returns>
            public virtual global::System.Threading.Tasks.Task StreamingFromServer(global::Grpc.Testing.SimpleRequest request, grpc::IServerStreamWriter<global::Grpc.Testing.SimpleResponse> responseStream, grpc::ServerCallContext context)
            {
                throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
            }

            /// <summary>
            /// Two-sided unbounded streaming between server to client
            /// Both sides send the content of their own choice to the other
            /// </summary>
            /// <param name="requestStream">Used for reading requests from the client.</param>
            /// <param name="responseStream">Used for sending responses back to the client.</param>
            /// <param name="context">The context of the server-side call handler being invoked.</param>
            /// <returns>A task indicating completion of the handler.</returns>
            public virtual global::System.Threading.Tasks.Task StreamingBothWays(grpc::IAsyncStreamReader<global::Grpc.Testing.SimpleRequest> requestStream, grpc::IServerStreamWriter<global::Grpc.Testing.SimpleResponse> responseStream, grpc::ServerCallContext context)
            {
                throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
            }

        }

        /// <summary>Creates service definition that can be registered with a server</summary>
        /// <param name="serviceImpl">An object implementing the server-side handling logic.</param>
        public static grpc::ServerServiceDefinition BindService(BenchmarkServiceBase serviceImpl)
        {
            return grpc::ServerServiceDefinition.CreateBuilder()
                .AddMethod(__Method_UnaryCall, serviceImpl.UnaryCall)
                .AddMethod(__Method_StreamingCall, serviceImpl.StreamingCall)
                .AddMethod(__Method_StreamingFromClient, serviceImpl.StreamingFromClient)
                .AddMethod(__Method_StreamingFromServer, serviceImpl.StreamingFromServer)
                .AddMethod(__Method_StreamingBothWays, serviceImpl.StreamingBothWays).Build();
        }

        /// <summary>Register service method with a service binder with or without implementation. Useful when customizing the  service binding logic.
        /// Note: this method is part of an experimental API that can change or be removed without any prior notice.</summary>
        /// <param name="serviceBinder">Service methods will be bound by calling <c>AddMethod</c> on this object.</param>
        /// <param name="serviceImpl">An object implementing the server-side handling logic.</param>
        public static void BindService(grpc::ServiceBinderBase serviceBinder, BenchmarkServiceBase serviceImpl)
        {
            serviceBinder.AddMethod(__Method_UnaryCall, serviceImpl == null ? null : new grpc::UnaryServerMethod<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(serviceImpl.UnaryCall));
            serviceBinder.AddMethod(__Method_StreamingCall, serviceImpl == null ? null : new grpc::DuplexStreamingServerMethod<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(serviceImpl.StreamingCall));
            serviceBinder.AddMethod(__Method_StreamingFromClient, serviceImpl == null ? null : new grpc::ClientStreamingServerMethod<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(serviceImpl.StreamingFromClient));
            serviceBinder.AddMethod(__Method_StreamingFromServer, serviceImpl == null ? null : new grpc::ServerStreamingServerMethod<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(serviceImpl.StreamingFromServer));
            serviceBinder.AddMethod(__Method_StreamingBothWays, serviceImpl == null ? null : new grpc::DuplexStreamingServerMethod<global::Grpc.Testing.SimpleRequest, global::Grpc.Testing.SimpleResponse>(serviceImpl.StreamingBothWays));
        }

    }
}
#endregion
