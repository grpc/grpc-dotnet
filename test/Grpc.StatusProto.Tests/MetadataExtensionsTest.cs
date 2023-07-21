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
/// Tests for MetadataExtensions
/// </summary>
public class MetadataExtensionsTest
{
    readonly Google.Rpc.Status status = new()
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
    public void SetRpcStatusTest()
    {
        var metadata = new Metadata();
        metadata.SetRpcStatus(status);

        var entry = metadata.Get(MetadataExtensions.StatusDetailsTrailerName);
        Assert.IsNotNull(entry);
        var sts = Google.Rpc.Status.Parser.ParseFrom(entry!.ValueBytes);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetRpcStatus_OK()
    {
        var metadata = new Metadata();
        metadata.SetRpcStatus(status);

        var sts = metadata.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetRpcStatus_NotFound()
    {
        var metadata = new Metadata();

        var sts = metadata.GetRpcStatus();
        Assert.IsNull(sts);
    }

    [Test]
    public void GetRpcStatus_BadEncoding()
    {
        var metadata = new Metadata();
        metadata.Add(MetadataExtensions.StatusDetailsTrailerName, new byte[] {1,2,3});

        var sts = metadata.GetRpcStatus();
        Assert.IsNull(sts);
    }

}
