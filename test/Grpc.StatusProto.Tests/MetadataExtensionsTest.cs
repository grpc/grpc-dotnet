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
using Google.Protobuf;

namespace Grpc.Core.Tests;

/// <summary>
/// Tests for MetadataExtensions
/// </summary>
[TestFixture]
public class MetadataExtensionsTest
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
    public void SetRpcStatusTest()
    {
        // Arrange
        var metadata = new Metadata();

        // Act
        metadata.SetRpcStatus(status);

        // Assert
        var entry = metadata.Get(MetadataExtensions.StatusDetailsTrailerName);
        Assert.IsNotNull(entry);
        var sts = Google.Rpc.Status.Parser.ParseFrom(entry!.ValueBytes);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void SetRpcStatus_MultipleTimes()
    {
        // Arrange
        Google.Rpc.Status status1 = new()
        {
            Code = (int)StatusCode.NotFound,
            Message = "first"
        };

        Google.Rpc.Status status2 = new()
        {
            Code = (int)StatusCode.NotFound,
            Message = "second"
        };

        Google.Rpc.Status status3 = new()
        {
            Code = (int)StatusCode.NotFound,
            Message = "third"
        };
        var metadata = new Metadata();

        // Act - set the status three times
        metadata.SetRpcStatus(status1);
        metadata.SetRpcStatus(status2);
        metadata.SetRpcStatus(status3);

        // Assert - only the last one should be in the metadata
        Assert.AreEqual(1, metadata.Count);

        var entry = metadata.Get(MetadataExtensions.StatusDetailsTrailerName);
        Assert.IsNotNull(entry);
        var sts = Google.Rpc.Status.Parser.ParseFrom(entry!.ValueBytes);
        Assert.AreEqual(status3, sts);
    }

    [Test]
    public void GetRpcStatus_OK()
    {
        // Arrange
        var metadata = new Metadata();
        metadata.SetRpcStatus(status);

        // Act - retrieve the status from the metadata
        var sts = metadata.GetRpcStatus();

        // Assert - status retrieved ok
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetRpcStatus_NotFound()
    {
        // Arrange
        var metadata = new Metadata();

        // Act - try and retrieve the non-existent status from the metadata
        var sts = metadata.GetRpcStatus();

        // Assert - not found
        Assert.IsNull(sts);
    }

    [Test]
    public void GetRpcStatus_BadEncoding()
    {
        // Arrange - create badly encoded status in the metadata
        var metadata = new Metadata
        {
            { MetadataExtensions.StatusDetailsTrailerName, new byte[] { 1, 2, 3 } }
        };

        // Act - try and retrieve the badly formed status from the metadata
        var sts = metadata.GetRpcStatus(ignoreParseError: true);

        // Assert - not found as it could not be decoded
        Assert.IsNull(sts);
    }

    [Test]
    public void GetRpcStatus_BadEncodingWithException()
    {
        // Arrange - create badly encoded status in the metadata
        var metadata = new Metadata
        {
            { MetadataExtensions.StatusDetailsTrailerName, new byte[] { 1, 2, 3 } }
        };

        // Act and Assert
        // Try and retrieve the status from the metadata and expect an exception
        // because it could not be decoded
        _ = Assert.Throws<InvalidProtocolBufferException>(() => metadata.GetRpcStatus(ignoreParseError: false));
    }

}
