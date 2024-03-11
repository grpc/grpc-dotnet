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

#if SUPPORT_LOAD_BALANCING
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.Tests.Shared;

internal class TestResolver : PollingResolver
{
    private readonly Func<Task>? _onRefreshAsync;
    private readonly TaskCompletionSource<object?> _hasResolvedTcs;
    private readonly ILogger _logger;
    private ResolverResult? _result;

    public Task HasResolvedTask => _hasResolvedTcs.Task;

    public TestResolver(ILoggerFactory loggerFactory) : this(loggerFactory, null)
    {
    }

    public TestResolver(ILoggerFactory? loggerFactory = null, Func<Task>? onRefreshAsync = null) : base(loggerFactory ?? NullLoggerFactory.Instance)
    {
        _onRefreshAsync = onRefreshAsync;
        _hasResolvedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _logger = (ILogger?)loggerFactory?.CreateLogger<TestResolver>() ?? NullLogger.Instance;
    }

    public void UpdateAddresses(List<BalancerAddress> addresses, ServiceConfig? serviceConfig = null, Status? serviceConfigStatus = null)
    {
        _logger.LogInformation("Updating result addresses: {Addresses}", string.Join(", ", addresses));
        UpdateResult(ResolverResult.ForResult(addresses, serviceConfig, serviceConfigStatus));
    }

    public void UpdateError(Status status)
    {
        _logger.LogInformation("Updating result error: {Status}", status);
        UpdateResult(ResolverResult.ForFailure(status));
    }

    public void UpdateResult(ResolverResult result)
    {
        _result = result;
        Listener?.Invoke(result);
    }

    protected override async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (_onRefreshAsync != null)
        {
            await _onRefreshAsync();
        }

        Listener(_result ?? ResolverResult.ForResult(Array.Empty<BalancerAddress>(), serviceConfig: null, serviceConfigStatus: null));
        _hasResolvedTcs.TrySetResult(null);
    }
}
#endif
