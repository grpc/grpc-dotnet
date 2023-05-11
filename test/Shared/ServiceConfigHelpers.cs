#region Copyright notice and license

// Copyright 2019 The gRPC Authors
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

using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace Grpc.Tests.Shared;

internal static class ServiceConfigHelpers
{
    public static ServiceConfig CreateRetryServiceConfig(
        int? maxAttempts = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        double? backoffMultiplier = null,
        IList<StatusCode>? retryableStatusCodes = null,
        RetryThrottlingPolicy? retryThrottling = null)
    {
        var retryPolicy = new RetryPolicy
        {
            MaxAttempts = maxAttempts ?? 5,
            InitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(1),
            MaxBackoff = maxBackoff ?? TimeSpan.FromMilliseconds(1),
            BackoffMultiplier = backoffMultiplier ?? 1
        };

        if (retryableStatusCodes != null)
        {
            foreach (var statusCode in retryableStatusCodes)
            {
                retryPolicy.RetryableStatusCodes.Add(statusCode);
            }
        }
        else
        {
            retryPolicy.RetryableStatusCodes.Add(StatusCode.Unavailable);
        }

        return new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = retryPolicy
                }
            },
            RetryThrottling = retryThrottling
        };
    }

    public static ServiceConfig CreateHedgingServiceConfig(
        int? maxAttempts = null,
        TimeSpan? hedgingDelay = null,
        IList<StatusCode>? nonFatalStatusCodes = null,
        RetryThrottlingPolicy? retryThrottling = null)
    {
        var hedgingPolicy = new HedgingPolicy
        {
            MaxAttempts = maxAttempts ?? 5,
            HedgingDelay = hedgingDelay
        };

        if (nonFatalStatusCodes != null)
        {
            foreach (var statusCode in nonFatalStatusCodes)
            {
                hedgingPolicy.NonFatalStatusCodes.Add(statusCode);
            }
        }
        else
        {
            hedgingPolicy.NonFatalStatusCodes.Add(StatusCode.Unavailable);
        }

        return new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    HedgingPolicy = hedgingPolicy
                }
            },
            RetryThrottling = retryThrottling
        };
    }
}
