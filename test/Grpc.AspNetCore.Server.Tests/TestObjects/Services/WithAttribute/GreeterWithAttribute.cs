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
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttribute
{
    public static partial class GreeterWithAttribute
    {
        static readonly string __ServiceName = "Greet.Greeter";

        static readonly Marshaller<global::Greet.HelloRequest> __Marshaller_Greet_HelloRequest = Marshallers.Create((arg) => global::Google.Protobuf.MessageExtensions.ToByteArray(arg), global::Greet.HelloRequest.Parser.ParseFrom);
        static readonly Marshaller<global::Greet.HelloReply> __Marshaller_Greet_HelloReply = Marshallers.Create((arg) => global::Google.Protobuf.MessageExtensions.ToByteArray(arg), global::Greet.HelloReply.Parser.ParseFrom);

        static readonly Method<global::Greet.HelloRequest, global::Greet.HelloReply> __Method_SayHello = new Method<global::Greet.HelloRequest, global::Greet.HelloReply>(
            MethodType.Unary,
            __ServiceName,
            "SayHello",
            __Marshaller_Greet_HelloRequest,
            __Marshaller_Greet_HelloReply);

        static readonly Method<global::Greet.HelloRequest, global::Greet.HelloReply> __Method_SayHellos = new Method<global::Greet.HelloRequest, global::Greet.HelloReply>(
            MethodType.ServerStreaming,
            __ServiceName,
            "SayHellos",
            __Marshaller_Greet_HelloRequest,
            __Marshaller_Greet_HelloReply);

        [BindServiceMethod(typeof(GreeterWithAttribute), "BindService")]
        public abstract partial class GreeterBase
        {
            public virtual global::System.Threading.Tasks.Task<global::Greet.HelloReply> SayHello(global::Greet.HelloRequest request, ServerCallContext context)
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, ""));
            }

            public virtual global::System.Threading.Tasks.Task SayHellos(global::Greet.HelloRequest request, IServerStreamWriter<global::Greet.HelloReply> responseStream, ServerCallContext context)
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, ""));
            }
        }

        public static ServerServiceDefinition BindService(GreeterBase serviceImpl)
        {
            throw new NotImplementedException();
        }

        public static void BindService(ServiceBinderBase serviceBinder, GreeterBase serviceImpl)
        {
            serviceBinder.AddMethod(__Method_SayHello, (UnaryServerMethod<global::Greet.HelloRequest, global::Greet.HelloReply>)null!);
            serviceBinder.AddMethod(__Method_SayHellos, (ServerStreamingServerMethod<global::Greet.HelloRequest, global::Greet.HelloReply>)null!);
        }
    }
}
