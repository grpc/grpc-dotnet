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

namespace Grpc.Net.Client.Internal;

/// <summary>
/// Cached log scope and URI for a gRPC <see cref="IMethod"/>.
/// </summary>
internal sealed class GrpcMethodInfo
{
    public GrpcMethodInfo(GrpcCallScope logScope, Uri callUri, MethodConfig? methodConfig)
    {
        LogScope = logScope;
        CallUri = callUri;
        MethodConfig = CreateMethodConfig(methodConfig);
    }

    private MethodConfigInfo? CreateMethodConfig(MethodConfig? methodConfig)
    {
        if (methodConfig == null)
        {
            return null;
        }
        if (methodConfig.RetryPolicy != null && methodConfig.HedgingPolicy != null)
        {
            throw new InvalidOperationException("Method config can't have a retry policy and hedging policy.");
        }

        var m = new MethodConfigInfo();

        if (methodConfig.RetryPolicy != null)
        {
            m.RetryPolicy = CreateRetryPolicy(methodConfig.RetryPolicy);
        }

        if (methodConfig.HedgingPolicy != null)
        {
            m.HedgingPolicy = CreateHedgingPolicy(methodConfig.HedgingPolicy);
        }

        return m;
    }

    internal static RetryPolicyInfo CreateRetryPolicy(RetryPolicy r)
    {
        if (!(r.MaxAttempts > 1))
        {
            throw new InvalidOperationException("Retry policy max attempts must be greater than 1.");
        }
        if (!(r.InitialBackoff > TimeSpan.Zero))
        {
            throw new InvalidOperationException("Retry policy initial backoff must be greater than zero.");
        }
        if (!(r.MaxBackoff > TimeSpan.Zero))
        {
            throw new InvalidOperationException("Retry policy maximum backoff must be greater than zero.");
        }
        if (!(r.BackoffMultiplier > 0))
        {
            throw new InvalidOperationException("Retry policy backoff multiplier must be greater than 0.");
        }
        if (!(r.RetryableStatusCodes.Count > 0))
        {
            throw new InvalidOperationException("Retry policy must specify at least 1 retryable status code.");
        }

        return new RetryPolicyInfo
        {
            MaxAttempts = r.MaxAttempts.Value,
            InitialBackoff = r.InitialBackoff.Value,
            MaxBackoff = r.MaxBackoff.Value,
            BackoffMultiplier = r.BackoffMultiplier.Value,
            RetryableStatusCodes = r.RetryableStatusCodes.ToList()
        };
    }

    internal static HedgingPolicyInfo CreateHedgingPolicy(HedgingPolicy h)
    {
        if (!(h.MaxAttempts > 1))
        {
            throw new InvalidOperationException("Hedging policy max attempts must be greater than 1.");
        }
        if (h.HedgingDelay != null && h.HedgingDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Hedging policy delay must be equal or greater than zero.");
        }

        return new HedgingPolicyInfo
        {
            MaxAttempts = h.MaxAttempts.Value,
            HedgingDelay = h.HedgingDelay ?? TimeSpan.Zero,
            NonFatalStatusCodes = h.NonFatalStatusCodes.ToList()
        };
    }

    public GrpcCallScope LogScope { get; }
    public Uri CallUri { get; }
    public MethodConfigInfo? MethodConfig { get; }
}

internal sealed class MethodConfigInfo
{
    public RetryPolicyInfo? RetryPolicy { get; set; }
    public HedgingPolicyInfo? HedgingPolicy { get; set; }
}

internal sealed class RetryPolicyInfo
{
    public int MaxAttempts { get; init; }
    public TimeSpan InitialBackoff { get; init; }
    public TimeSpan MaxBackoff { get; init; }
    public double BackoffMultiplier { get; init; }
    public List<StatusCode> RetryableStatusCodes { get; init; } = default!;
}

internal sealed class HedgingPolicyInfo
{
    public int MaxAttempts { get; set; }
    public TimeSpan HedgingDelay { get; set; }
    public List<StatusCode> NonFatalStatusCodes { get; init; } = default!;
}
