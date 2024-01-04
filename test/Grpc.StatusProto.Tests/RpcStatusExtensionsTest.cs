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

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using NUnit.Framework;

namespace Grpc.Core.Tests;

/// <summary>
/// Tests for RpcStatusExtensions
/// </summary>
[TestFixture]
public class RpcStatusExtensionsTest
{
    [Test]
    public void ToRpcExceptionTest()
    {
        // Arrange - create a status
        var status = CreateFullStatus();
        // Act - get exception from the status
        var ex = status.ToRpcException();

        // Assert - check the details in the exception
        Assert.IsNotNull(ex);

        var grpcSts = ex.Status;
        Assert.AreEqual(StatusCode.ResourceExhausted, grpcSts.StatusCode);
        Assert.AreEqual("Test", grpcSts.Detail);

        var sts = ex.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void ToRpcExceptionWithParamsTest()
    {
        // Arrange - create a status
        var status = CreateFullStatus();

        // Act - get exception from the status with specific parameters
        var ex = status.ToRpcException(StatusCode.Cancelled, "status message");
        Assert.IsNotNull(ex);

        // Assert - check the details in the exception
        var grpcSts = ex.Status;
        Assert.AreEqual(StatusCode.Cancelled, grpcSts.StatusCode);
        Assert.AreEqual("status message", grpcSts.Detail);

        var sts = ex.GetRpcStatus();
        Assert.IsNotNull(sts);
        Assert.AreEqual(status, sts);
    }

    [Test]
    public void GetStatusDetailTest()
    {
        // Arrange - create a status
        // The detailsMap contains all the Messages added to the status so
        // these can be used in the comparisions when then are retrieved later
        var detailsMap = new Dictionary<string, IMessage>();
        var status = CreateFullStatus(detailsMap);

        // Act
        var badRequest = status.GetDetail<BadRequest>();
        // Assert
        Assert.IsNotNull(badRequest);
        var expected = detailsMap["badRequest"];
        Assert.AreEqual(expected, badRequest);

        // Act
        var errorInfo = status.GetDetail<ErrorInfo>();
        // Assert
        Assert.IsNotNull(errorInfo);
        expected = detailsMap["errorInfo"];
        Assert.AreEqual(expected, errorInfo);

        // Act
        var retryInfo = status.GetDetail<RetryInfo>();
        // Assert
        Assert.IsNotNull(retryInfo);
        expected = detailsMap["retryInfo"];
        Assert.AreEqual(expected, retryInfo);

        // Act
        var debugInfo = status.GetDetail<DebugInfo>();
        // Assert
        Assert.IsNotNull(debugInfo);
        expected = detailsMap["debugInfo"];
        Assert.AreEqual(expected, debugInfo);

        // Act
        var quotaFailure = status.GetDetail<QuotaFailure>();
        // Assert
        Assert.IsNotNull(quotaFailure);
        expected = detailsMap["quotaFailure"];
        Assert.AreEqual(expected, quotaFailure);

        // Act
        var preconditionFailure = status.GetDetail<PreconditionFailure>();
        // Assert
        Assert.IsNotNull(preconditionFailure);
        expected = detailsMap["preconditionFailure"];
        Assert.AreEqual(expected, preconditionFailure);

        // Act
        var requestInfo = status.GetDetail<RequestInfo>();
        // Assert
        Assert.IsNotNull(requestInfo);
        expected = detailsMap["requestInfo"];
        Assert.AreEqual(expected, requestInfo);

        // Act
        var help = status.GetDetail<Help>();
        // Assert
        Assert.IsNotNull(help);
        expected = detailsMap["help"];
        Assert.AreEqual(expected, help);

        // Act
        var localizedMessage = status.GetDetail<LocalizedMessage>();
        // Assert
        Assert.IsNotNull(localizedMessage);
        expected = detailsMap["localizedMessage"];
        Assert.AreEqual(expected, localizedMessage);
    }

    [Test]
    public void GetStatusDetail_NotFound()
    {
        // Arrange - create a status with only a few details
        // The detailsMap contains all the Messages added to the status so
        // these can be used in the comparisions when then are retrieved later
        var detailsMap = new Dictionary<string, IMessage>();
        var status = CreatePartialStatus(detailsMap);

        // Act - try and retieve non-existent BadRequest from the status
        var badRequest = status.GetDetail<BadRequest>();
        // Assert
        Assert.IsNull(badRequest);
    }

    [Test]
    public void UnpackDetailMessageTest()
    {
        // Arrange - create a status
        // The detailsMap contains all the Messages added to the status so
        // these can be used in the comparisions when then are retrieved later
        var detailsMap = new Dictionary<string, IMessage>();
        var status = CreateFullStatus(detailsMap);

        // foundSet will contain the messages found in the status so we can
        // check all those expected were present
        var foundSet = new HashSet<string>();

        // Act and Assert - iterate over all the messages in the status
        // and check they contain what is expected
        foreach (var msg in status.UnpackDetailMessages())
        {
            switch (msg)
            {
                case ErrorInfo errorInfo:
                    {
                        var expected = detailsMap["errorInfo"];
                        Assert.AreEqual(expected, errorInfo);
                        foundSet.Add("errorInfo");
                        break;
                    }
                    
                case BadRequest badRequest:
                    {
                        var expected = detailsMap["badRequest"];
                        Assert.AreEqual(expected, badRequest);
                        foundSet.Add("badRequest");
                        break;
                    }

                case RetryInfo retryInfo:
                    {
                        var expected = detailsMap["retryInfo"];
                        Assert.AreEqual(expected, retryInfo);
                        foundSet.Add("retryInfo");
                        break;
                    }

                case DebugInfo debugInfo:
                    {
                        var expected = detailsMap["debugInfo"];
                        Assert.AreEqual(expected, debugInfo);
                        foundSet.Add("debugInfo");
                        break;
                    }

                case QuotaFailure quotaFailure:
                    {
                        var expected = detailsMap["quotaFailure"];
                        Assert.AreEqual(expected, quotaFailure);
                        foundSet.Add("quotaFailure");
                        break;
                    }

                case PreconditionFailure preconditionFailure:
                    {
                        var expected = detailsMap["preconditionFailure"];
                        Assert.AreEqual(expected, preconditionFailure);
                        foundSet.Add("preconditionFailure");
                        break;
                    }

                case RequestInfo requestInfo:
                    {
                        var expected = detailsMap["requestInfo"];
                        Assert.AreEqual(expected, requestInfo);
                        foundSet.Add("requestInfo");
                        break;
                    }

                case ResourceInfo resourceInfo:
                    {
                        var expected = detailsMap["resourceInfo"];
                        Assert.AreEqual(expected, resourceInfo);
                        foundSet.Add("resourceInfo");
                        break;
                    }

                case Help help:
                    {
                        var expected = detailsMap["help"];
                        Assert.AreEqual(expected, help);
                        foundSet.Add("help");
                        break;
                    }

                case LocalizedMessage localizedMessage:
                    {
                        var expected = detailsMap["localizedMessage"];
                        Assert.AreEqual(expected, localizedMessage);
                        foundSet.Add("localizedMessage");
                        break;
                    }
            }
        }

        // check everything was returned
        Assert.AreEqual(detailsMap.Count, foundSet.Count);

    }

    private static Google.Rpc.Status CreatePartialStatus(Dictionary<string, IMessage>? detailsMap = null)
    {
        var retryInfo = new RetryInfo
        {
            RetryDelay = Duration.FromTimeSpan(new TimeSpan(0, 0, 5))
        };

        var debugInfo = new DebugInfo()
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

        var status = new Google.Rpc.Status()
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

    static Google.Rpc.Status CreateFullStatus(Dictionary<string, IMessage>? detailsMap = null)
    {
        var errorInfo = new ErrorInfo()
        {
            Domain = "Rich Error Model Demo",
            Reason = "Full error requested in the demo",
            Metadata =
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }
        };

        var badRequest = new BadRequest()
        {
            FieldViolations =
            {
                new BadRequest.Types.FieldViolation()
                {
                    Field = "field", Description = "description"
                }
            }
        };

        var retryInfo = new RetryInfo
        {
            RetryDelay = Duration.FromTimeSpan(new TimeSpan(0, 0, 5))
        };

        var debugInfo = new DebugInfo()
        {
            StackEntries = { "stack1", "stack2" },
            Detail = "detail"
        };

        var quotaFailure = new QuotaFailure()
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

        var preconditionFailure = new PreconditionFailure()
        {
            Violations =
            {
                new PreconditionFailure.Types.Violation()
                {
                    Type = "type", Subject = "subject", Description = "description"
                }
            }
        };

        var requestInfo = new RequestInfo()
        {
            RequestId = "reqId",
            ServingData = "data"
        };

        var resourceInfo = new ResourceInfo()
        {
            ResourceType = "type",
            ResourceName = "name",
            Owner = "owner",
            Description = "description"
        };

        var help = new Help()
        {
            Links =
            {
                new Help.Types.Link() { Url="url1", Description="desc1" },
                new Help.Types.Link() { Url="url2", Description="desc2" },
            }
        };

        var localizedMessage = new LocalizedMessage()
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

        var status = new Google.Rpc.Status()
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
