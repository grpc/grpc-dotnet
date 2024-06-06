#region Copyright notice and license

// Copyright 2022 The gRPC Authors
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
using NUnit.Framework;

namespace Grpc.Core.Tests;

public class RpcExceptionTest
{
    [Test]
    public void StatusDebugExceptionPopulatesInnerException()
    {
        Assert.IsNull(new RpcException(new Status(StatusCode.Internal, "abc")).InnerException);

        var debugException = new ArgumentException("test exception");
        var ex = new RpcException(new Status(StatusCode.Internal, "abc", debugException));
        Assert.AreSame(debugException, ex.InnerException);
    }

    [Test]
    public void DefaultMessageDoesntContainDebugExceptionStacktrace()
    {
        Exception someExceptionWithStacktrace;
        try
        {
            throw new ArgumentException("test exception");
        }
        catch (Exception caughtEx)
        {
            someExceptionWithStacktrace = caughtEx;
        }
        var ex = new RpcException(new Status(StatusCode.Internal, "abc", someExceptionWithStacktrace));
        // Check debug exceptions's message is contained.
        StringAssert.Contains(someExceptionWithStacktrace.Message, ex.Message);
        StringAssert.Contains(someExceptionWithStacktrace.GetType().FullName!, ex.Message);
        // If name of the current method is not in the message, it probably doesn't contain the stack trace.
        StringAssert.DoesNotContain(nameof(DefaultMessageDoesntContainDebugExceptionStacktrace), ex.Message);
    }
}
