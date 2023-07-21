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
using Type = System.Type;

namespace Grpc.StatusProto.Tests;

/// <summary>
/// Tests for DetailsTypesRegistry
/// </summary>
public class DetailsTypesRegistryTest
{

    [Test]
    public void Unpack_ErrorInfo()
    {
        var any = Any.Pack(new ErrorInfo());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<ErrorInfo>(msg);
    }

    [Test]
    public void Unpack_BadRequest()
    {
        var any = Any.Pack(new BadRequest());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<BadRequest>(msg);
    }

    [Test]
    public void Unpack_RetryInfo()
    {
        var any = Any.Pack(new RetryInfo());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<RetryInfo>(msg);
    }

    [Test]
    public void Unpack_DebugInfo()
    {
        var any = Any.Pack(new DebugInfo());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<DebugInfo>(msg);
    }

    [Test]
    public void Unpack_QuotaFailure()
    {
        var any = Any.Pack(new QuotaFailure());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<QuotaFailure>(msg);
    }

    [Test]
    public void Unpack_PreconditionFailure()
    {
        var any = Any.Pack(new PreconditionFailure());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<PreconditionFailure>(msg);
    }

    [Test]
    public void Unpack_RequestInfo()
    {
        var any = Any.Pack(new RequestInfo());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<RequestInfo>(msg);
    }

    [Test]
    public void Unpack_ResourceInfo()
    {
        var any = Any.Pack(new ResourceInfo());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<ResourceInfo>(msg);
    }

    [Test]
    public void Unpack_Help()
    {
        var any = Any.Pack(new Help());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<Help>(msg);
    }

    [Test]
    public void Unpack_LocalizedMessage()
    {
        var any = Any.Pack(new LocalizedMessage());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNotNull(msg);
        Assert.IsInstanceOf<LocalizedMessage>(msg);
    }

    [Test]
    public void Unpack_Unknown()
    {
        // Timestamp is not one of the types in the registry
        var any = Any.Pack(new Timestamp());
        var msg = DetailsTypesRegistry.Unpack(any);
        Assert.IsNull(msg);
    }
}
