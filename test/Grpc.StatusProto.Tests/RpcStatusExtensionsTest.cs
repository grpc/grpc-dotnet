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
using System.Net.NetworkInformation;
using Google.Api;

namespace Grpc.StatusProto.Tests;

/// <summary>
/// Tests for RpcStatusExtensions
/// </summary>
public class RpcStatusExtensionsTest
{
    [Test]
    public void ToRpcExcetionTest()
    {
        Google.Rpc.Status status = CreateFullStatus();
        RpcException ex = status.ToRpcException();
        Assert.IsNotNull(ex);

        var grpcSts = ex.Status;
        Assert.AreEqual(StatusCode.ResourceExhausted, grpcSts.StatusCode);
        Assert.AreEqual("Test", grpcSts.Detail);

        var sts = ex.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetStatusDetailsTest()
    {
        var detailsMap = new Dictionary<string, IMessage>();
        Google.Rpc.Status status = CreateFullStatus(detailsMap);

        BadRequest? badRequest = status.GetBadRequest();
        Assert.IsNotNull(badRequest);
        IMessage expected = detailsMap["badRequest"];
        Assert.AreEqual(expected, badRequest);

        ErrorInfo? errorInfo = status.GetErrorInfo();
        Assert.IsNotNull(errorInfo);
        expected = detailsMap["errorInfo"];
        Assert.AreEqual(expected, errorInfo);

        RetryInfo? retryInfo = status.GetRetryInfo();
        Assert.IsNotNull(retryInfo);
        expected = detailsMap["retryInfo"];
        Assert.AreEqual(expected, retryInfo);

        DebugInfo? debugInfo = status.GetDebugInfo();
        Assert.IsNotNull(debugInfo);
        expected = detailsMap["debugInfo"];
        Assert.AreEqual(expected, debugInfo);

        QuotaFailure? quotaFailure = status.GetQuotaFailure();
        Assert.IsNotNull(quotaFailure);
        expected = detailsMap["quotaFailure"];
        Assert.AreEqual(expected, quotaFailure);

        PreconditionFailure? preconditionFailure = status.GetPreconditionFailure();
        Assert.IsNotNull(preconditionFailure);
        expected = detailsMap["preconditionFailure"];
        Assert.AreEqual(expected, preconditionFailure);

        RequestInfo? requestInfo = status.GetRequestInfo();
        Assert.IsNotNull(requestInfo);
        expected = detailsMap["requestInfo"];
        Assert.AreEqual(expected, requestInfo);

        Help? help = status.GetHelp();
        Assert.IsNotNull(help);
        expected = detailsMap["help"];
        Assert.AreEqual(expected, help);

        LocalizedMessage? localizedMessage = status.GetLocalizedMessage();
        Assert.IsNotNull(localizedMessage);
        expected = detailsMap["localizedMessage"];
        Assert.AreEqual(expected, localizedMessage);
    }

    [Test]
    public void GetStatusDetails_NotFound()
    {
        var detailsMap = new Dictionary<string, IMessage>();
        Google.Rpc.Status status = CreatePartialStatus(detailsMap);

        BadRequest? badRequest = status.GetBadRequest();
        Assert.IsNull(badRequest);
    }

    Google.Rpc.Status CreatePartialStatus(Dictionary<string, IMessage>? detailsMap = null)
    {
        RetryInfo retryInfo = new RetryInfo();
        retryInfo.RetryDelay = Duration.FromTimeSpan(new TimeSpan(0, 0, 5));

        DebugInfo debugInfo = new DebugInfo()
        {
            StackEntries = { "stack1", "stack2" },
            Detail = "detail"
        };

        // add details to a map for later checking
        if (detailsMap != null)
        {
            detailsMap.Clear();
            detailsMap.Add("retryInfo", retryInfo);
            detailsMap.Add("debugInfo", debugInfo);
        }

        Google.Rpc.Status status = new Google.Rpc.Status()
        {
            Code = (int)StatusCode.Unavailable,
            Message = "partial status",
            Details =
            {
                Any.Pack(retryInfo),
                Any.Pack(debugInfo),
            }
        };

        return status;
    }

    Google.Rpc.Status CreateFullStatus(Dictionary<string, IMessage>? detailsMap = null)
    {
        ErrorInfo errorInfo = new ErrorInfo()
        {
            Domain = "Rich Error Model Demo",
            Reason = "Full error requested in the demo",
            Metadata =
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }
        };

        BadRequest badRequest = new BadRequest()
        {
            FieldViolations =
            {
                new BadRequest.Types.FieldViolation()
                {
                    Field = "field", Description = "description"
                }
            }
        };

        RetryInfo retryInfo = new RetryInfo();
        retryInfo.RetryDelay = Duration.FromTimeSpan(new TimeSpan(0, 0, 5));

        DebugInfo debugInfo = new DebugInfo()
        {
            StackEntries = { "stack1", "stack2" },
            Detail = "detail"
        };

        QuotaFailure quotaFailure = new QuotaFailure()
        {
            Violations =
            {
                new QuotaFailure.Types.Violation()
                {
                    Description =  "Too much disk space used",
                    Subject = "Disk23"
                }
            }
        };

        PreconditionFailure preconditionFailure = new PreconditionFailure()
        {
            Violations =
            {
                new PreconditionFailure.Types.Violation()
                {
                    Type = "type", Subject = "subject", Description = "description"
                }
            }
        };

        RequestInfo requestInfo = new RequestInfo()
        {
            RequestId = "reqId",
            ServingData = "data"
        };

        ResourceInfo resourceInfo = new ResourceInfo()
        {
            ResourceType = "type",
            ResourceName = "name",
            Owner = "owner",
            Description = "description"
        };

        Help help = new Help()
        {
            Links =
            {
                new Help.Types.Link() { Url="url1", Description="desc1" },
                new Help.Types.Link() { Url="url2", Description="desc2" },
            }
        };

        LocalizedMessage localizedMessage = new LocalizedMessage()
        {
            Locale = "en-GB",
            Message = "Example localised error message"
        };

        // add details to a map for later checking
        if (detailsMap != null)
        {
            detailsMap.Clear();
            detailsMap.Add("badRequest", badRequest);
            detailsMap.Add("errorInfo", errorInfo);
            detailsMap.Add("retryInfo", retryInfo);
            detailsMap.Add("debugInfo", debugInfo);
            detailsMap.Add("quotaFailure", quotaFailure);
            detailsMap.Add("preconditionFailure", preconditionFailure);
            detailsMap.Add("requestInfo", requestInfo);
            detailsMap.Add("resourceInfo", resourceInfo);
            detailsMap.Add("help", help);
            detailsMap.Add("localizedMessage", localizedMessage);
        }

        Google.Rpc.Status status = new Google.Rpc.Status()
        {
            Code = (int)StatusCode.ResourceExhausted,
            Message = "Test",
            Details =
            {
                Any.Pack(badRequest),
                Any.Pack(errorInfo),
                Any.Pack(retryInfo),
                Any.Pack(debugInfo),
                Any.Pack(quotaFailure),
                Any.Pack(preconditionFailure),
                Any.Pack(requestInfo),
                Any.Pack(resourceInfo),
                Any.Pack(help),
                Any.Pack(localizedMessage)
            }
        };

        return status;
    }
}
