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

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.StatusProto;
using NUnit.Framework;
using Grpc.Core;
using NUnit.Framework.Constraints;

namespace Grpc.StatusProto.Tests;

/// <summary>
/// Tests for RpcExceptionExtensions
/// </summary>
public class RpcExceptionExtensionsTest
{
    readonly Google.Rpc.Status status = new Google.Rpc.Status()
    {
        Code = (int)StatusCode.NotFound,
        Message = "Simple error message",
        Details =
            {
                Any.Pack(new ErrorInfo
                {
                    Domain = "some domain",
                    Reason = "a reason"
                }),
                Any.Pack(new RequestInfo
                {
                    RequestId = "request id",
                    ServingData = "data"
                }),
            }
    };

    [Test]
    public void GetRpcStatus_OK()
    {
        RpcException exception = status.ToRpcException();
        var sts = exception.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetRpcStatus_NotFound()
    {
        RpcException exception = new RpcException(new Core.Status());
        var sts = exception.GetRpcStatus();
        Assert.IsNull(sts);
    }
}
