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
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.HealthChecks.Internal;

internal sealed class HealthServiceIntegration : Grpc.Health.V1.Health.HealthBase
{
    private readonly HealthCheckOptions _healthCheckOptions;
    private readonly GrpcHealthChecksOptions _grpcHealthCheckOptions;
    private readonly HealthServiceImpl _healthServiceImpl;
    private readonly HealthCheckService _healthCheckService;

    public HealthServiceIntegration(
        HealthServiceImpl healthServiceImpl,
        IOptions<HealthCheckOptions> healthCheckOptions,
        IOptions<GrpcHealthChecksOptions> grpcHealthCheckOptions,
        HealthCheckService healthCheckService)
    {
        _healthCheckOptions = healthCheckOptions.Value;
        _grpcHealthCheckOptions = grpcHealthCheckOptions.Value;
        _healthServiceImpl = healthServiceImpl;
        _healthCheckService = healthCheckService;
    }

    public override async Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context)
    {
        if (_grpcHealthCheckOptions.RunHealthChecksOnCheck)
        {
            // Match Check behavior from Grpc.HealthCheck.HealthServiceImpl.
            if (_grpcHealthCheckOptions.Services.TryGetServiceMapping(request.Service, out var serviceMapping))
            {
                var result = await _healthCheckService.CheckHealthAsync(_healthCheckOptions.Predicate, context.CancellationToken);

                return new HealthCheckResponse
                {
                    Status = HealthChecksStatusHelpers.GetStatus(result, serviceMapping.Predicate)
                };
            }

            throw new RpcException(new Status(StatusCode.NotFound, ""));
        }
        else
        {
            return await _healthServiceImpl.Check(request, context);
        }
    }

    public override Task Watch(HealthCheckRequest request, IServerStreamWriter<HealthCheckResponse> responseStream, ServerCallContext context)
    {
        return _healthServiceImpl.Watch(request, responseStream, context);
    }
}
