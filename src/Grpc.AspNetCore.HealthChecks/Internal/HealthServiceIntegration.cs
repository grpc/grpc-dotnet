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

using System.Diagnostics;
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

    public override Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context)
    {
        if (!_grpcHealthCheckOptions.UseHealthChecksCache)
        {
            return GetHealthCheckResponseAsync(request.Service, throwOnNotFound: true, context.CancellationToken);
        }
        else
        {
            return _healthServiceImpl.Check(request, context);
        }
    }

    public override Task Watch(HealthCheckRequest request, IServerStreamWriter<HealthCheckResponse> responseStream, ServerCallContext context)
    {
        if (!_grpcHealthCheckOptions.UseHealthChecksCache)
        {
            // Stream writer replaces first health checks results from the cache with newly calculated health check results.
            responseStream = new WatchServerStreamWriter(this, request, responseStream, context.CancellationToken);
        }

        return _healthServiceImpl.Watch(request, responseStream, context);
    }

    private async Task<HealthCheckResponse> GetHealthCheckResponseAsync(string service, bool throwOnNotFound, CancellationToken cancellationToken)
    {
        // Match Check behavior from Grpc.HealthCheck.HealthServiceImpl.
        HealthCheckResponse.Types.ServingStatus status;
        if (_grpcHealthCheckOptions.Services.TryGetServiceMapping(service, out var serviceMapping))
        {
            var result = await _healthCheckService.CheckHealthAsync((HealthCheckRegistration registration) =>
            {
                if (_healthCheckOptions.Predicate != null && !_healthCheckOptions.Predicate(registration))
                {
                    return false;
                }

                if (serviceMapping.FilterPredicate != null && !serviceMapping.FilterPredicate(new HealthCheckFilterContext(registration.Name, registration.Tags)))
                {
                    return false;
                }

                return true;
            }, cancellationToken);

#pragma warning disable CS0618 // Type or member is obsolete
            var results = result.Entries.Select(entry => new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data));
            if (serviceMapping.Predicate != null)
            {
                results = results.Where(serviceMapping.Predicate);
            }
            (status, _) = HealthChecksStatusHelpers.GetStatus(results);
#pragma warning restore CS0618 // Type or member is obsolete
        }
        else
        {
            if (throwOnNotFound)
            {
                throw new RpcException(new Status(StatusCode.NotFound, ""));
            }
            else
            {
                status = HealthCheckResponse.Types.ServingStatus.ServiceUnknown;
            }
        }

        return new HealthCheckResponse { Status = status };
    }

    /// <summary>
    /// The stream writer intercepts and replaces the first watch results because they're cached values.
    /// Newly calculated values from .NET health checks are returned instead.
    /// </summary>
    private sealed class WatchServerStreamWriter : IServerStreamWriter<HealthCheckResponse>
    {
        private readonly HealthServiceIntegration _service;
        private readonly HealthCheckRequest _request;
        private readonly IServerStreamWriter<HealthCheckResponse> _innerResponseStream;
        private readonly CancellationToken _cancellationToken;
        private bool _receivedFirstWrite;

        public WriteOptions? WriteOptions
        {
            get => _innerResponseStream.WriteOptions;
            set => _innerResponseStream.WriteOptions = value;
        }

        public WatchServerStreamWriter(HealthServiceIntegration service, HealthCheckRequest request, IServerStreamWriter<HealthCheckResponse> responseStream, CancellationToken cancellationToken)
        {
            Debug.Assert(!service._grpcHealthCheckOptions.UseHealthChecksCache);

            _service = service;
            _request = request;
            _innerResponseStream = responseStream;
            _cancellationToken = cancellationToken;
        }

        public async Task WriteAsync(HealthCheckResponse message)
        {
            // Replace first results.
            if (!_receivedFirstWrite)
            {
                _receivedFirstWrite = true;
                message = await _service.GetHealthCheckResponseAsync(_request.Service, throwOnNotFound: false, _cancellationToken);
            }

            await _innerResponseStream.WriteAsync(message);
        }
    }
}
