#region Copyright notice and license
// Copyright 2023 gRPC authors.
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

using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using NUnit.Framework;

namespace Grpc.Core.Tests;

/// <summary>
/// Tests for RpcExceptionExtensions
/// </summary>
[TestFixture]
public class RpcExceptionExtensionsTest
{
    // creates a status to use in the tests
    private readonly Google.Rpc.Status status = new()
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
        // Act
        var exception = status.ToRpcException();

        // Assert - check the contents of the exception
        Assert.AreEqual(status.Code, (int)exception.StatusCode);
        Assert.AreEqual(status.Message, exception.Status.Detail);
        var sts = exception.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetRpcStatus_NotFound()
    {
        // Act
        var exception = new RpcException(new Core.Status());

        // Assert - the exception does not contain a RpcStatus
        var sts = exception.GetRpcStatus();
        Assert.IsNull(sts);
    }

    [Test]
    public void GetRpcStatus_SetCodeAndMessage()
    {
        // Arrange and Act - create the exception with status code and message
        var exception = status.ToRpcException(StatusCode.Aborted, "Different message");

        // Assert - check the details in the exception
        Assert.AreEqual(StatusCode.Aborted, exception.StatusCode);
        Assert.AreEqual("Different message", exception.Status.Detail);
        var sts = exception.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }
}
