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

using System.Text;
using Google.Rpc;
using NUnit.Framework;

namespace Grpc.StatusProto.Tests;

/// <summary>
/// Tests for ExceptionExtensions
/// </summary>
public class ExceptionExtensionsTest
{
    
    [Test]
    public void ToRpcDebugInfoTest()
    {
        try
        {
            ThrowException("extra details");
        }
        catch (Exception ex)
        {
            var debugInfo = ex.ToRpcDebugInfo();
            Assert.IsNotNull(debugInfo);
            Assert.AreEqual("System.ArgumentException: extra details", debugInfo.Detail);

            var stackTraces = ConcatStackTraces(debugInfo);
            Console.WriteLine("Test stack trace data:");
            Console.WriteLine(stackTraces);
            Assert.IsTrue(stackTraces.Contains("ExceptionExtensionsTest.ThrowException"));
            Assert.IsTrue(stackTraces.Contains("ExceptionExtensionsTest.ToRpcDebugInfoTest"));
            Assert.IsFalse(stackTraces.Contains("InnerException:"));
        }
    }

    [Test]
    public void ToRpcDebugInfo_WithInnerExceptionTest()
    {
        try
        {
            ThrowException("extra details");
        }
        catch (Exception ex)
        {
            var debugInfo = ex.ToRpcDebugInfo(1);
            Assert.IsNotNull(debugInfo);
            Assert.AreEqual("System.ArgumentException: extra details", debugInfo.Detail);

            var stackTraces = ConcatStackTraces(debugInfo);
            Console.WriteLine("Test stack trace data:");
            Console.WriteLine(stackTraces);
            Assert.IsTrue(stackTraces.Contains("ExceptionExtensionsTest.ThrowException"));
            Assert.IsTrue(stackTraces.Contains("ExceptionExtensionsTest.ToRpcDebugInfo_WithInnerExceptionTest"));
            Assert.IsTrue(stackTraces.Contains("InnerException: System.ApplicationException: inner exception"));
        }
    }

    private void ThrowException(string message)
    {
        try
        {
            ThrowInnerException("inner exception");
        }
        catch (Exception ex)
        {
            throw new ArgumentException(message, ex);
        }
    }

    private void ThrowInnerException(string message)
    {
        throw new System.ApplicationException(message);
    }

    private string ConcatStackTraces(DebugInfo debugInfo)
    {
        var sb = new StringBuilder();

        foreach (var stackEntry in debugInfo.StackEntries)
        {
            sb.AppendLine(stackEntry);
        }

        return sb.ToString();
    }
}
