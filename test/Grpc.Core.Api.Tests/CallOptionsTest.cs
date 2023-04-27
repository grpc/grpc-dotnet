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
using System.Threading;
using Grpc.Core.Internal;
using NUnit.Framework;

namespace Grpc.Core.Tests;

public class CallOptionsTest
{
    [Test]
    public void WithMethods()
    {
        var options = new CallOptions();
        
        var metadata = new Metadata();
        Assert.AreSame(metadata, options.WithHeaders(metadata).Headers);

        var deadline = DateTime.UtcNow;
        Assert.AreEqual(deadline, options.WithDeadline(deadline).Deadline!.Value);

        var cancellationToken = new CancellationTokenSource().Token;
        Assert.AreEqual(cancellationToken, options.WithCancellationToken(cancellationToken).CancellationToken);

        var writeOptions = new WriteOptions();
        Assert.AreSame(writeOptions, options.WithWriteOptions(writeOptions).WriteOptions);

        // TODO(jtattermusch): add back test for .WithPropagationToken() method
        // var propagationToken = new ContextPropagationTokenImpl(CallSafeHandle.NullInstance, DateTime.UtcNow,
        //     CancellationToken.None, ContextPropagationOptions.Default);
        // Assert.AreSame(propagationToken, options.WithPropagationToken(propagationToken).PropagationToken);

        var credentials = new FakeCallCredentials();
        Assert.AreSame(credentials, options.WithCredentials(credentials).Credentials);

        var flags = CallFlags.WaitForReady | CallFlags.CacheableRequest;
        Assert.AreEqual(flags, options.WithFlags(flags).Flags);

        // Check that the original instance is unchanged.
        Assert.IsNull(options.Headers);
        Assert.IsNull(options.Deadline);
        Assert.AreEqual(CancellationToken.None, options.CancellationToken);
        Assert.IsNull(options.WriteOptions);
        Assert.IsNull(options.PropagationToken);
        Assert.IsNull(options.Credentials);
        Assert.AreEqual(default(CallFlags), options.Flags);
    }

    [Test]
    public void WaitForReady()
    {
        var callOptions = new CallOptions();
        Assert.IsFalse(callOptions.IsWaitForReady);

        Assert.AreEqual(CallFlags.WaitForReady, callOptions.WithWaitForReady().Flags);
        Assert.IsTrue(callOptions.WithWaitForReady().IsWaitForReady);
        Assert.IsFalse(callOptions.WithWaitForReady(true).WithWaitForReady(false).IsWaitForReady);
    }
}
