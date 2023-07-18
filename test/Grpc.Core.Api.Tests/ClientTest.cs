#region Copyright notice and license

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

#endregion

using System;
using Grpc.Core.Internal;
using NUnit.Framework;

namespace Grpc.Core.Tests;

public class ClientTest
{
    [Test]
    public void NonGeneratedClient()
    {
        var client = new TestClient();

        var debugType = new ClientBase.ClientBaseDebugType(client);

        Assert.AreEqual(null, debugType.Service);
        Assert.AreEqual(null, debugType.Methods);
    }

    [Test]
    public void GeneratedClient()
    {
        var client = new Greeter.GreeterClient(new UnimplementedCallInvoker());

        var debugType = new ClientBase.ClientBaseDebugType(client);

        Assert.AreEqual("greet.Greeter", debugType.Service);
        Assert.AreEqual(1, debugType.Methods!.Count);
        Assert.AreEqual("SayHello", debugType.Methods[0].Name);
    }

    private class TestClient : ClientBase
    {
    }

    public static partial class Greeter
    {
        static readonly string __ServiceName = "greet.Greeter";

        static readonly Marshaller<object> __Marshaller = Marshallers.Create(_ => Array.Empty<byte>(), _ => new object());

        static readonly Method<object, object> __Method_SayHello = new Method<object, object>(
            MethodType.Unary,
            __ServiceName,
            "SayHello",
            __Marshaller,
            __Marshaller);

        public partial class GreeterClient : ClientBase<GreeterClient>
        {
            public GreeterClient(CallInvoker callInvoker) : base(callInvoker)
            {
            }

            protected GreeterClient(ClientBaseConfiguration configuration) : base(configuration)
            {
            }

            public virtual AsyncUnaryCall<object> SayHelloAsync(object request, CallOptions options)
            {
                return CallInvoker.AsyncUnaryCall(__Method_SayHello, null, options, request);
            }

            protected override GreeterClient NewInstance(ClientBaseConfiguration configuration)
            {
                return new GreeterClient(configuration);
            }
        }
    }
}
