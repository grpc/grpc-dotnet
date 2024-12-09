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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.HealthChecks.Internal;

internal sealed class HealthServiceIntegration : Grpc.Health.V1.Health.HealthBase
{
    private readonly HealthCheckOptions _healthCheckOptions;
    private readonly GrpcHealthChecksOptions _grpcHealthCheckOptions;
    private readonly HealthServiceImpl _healthServiceImpl;
    private readonly HealthCheckService _healthCheckService;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public HealthServiceIntegration(
        HealthServiceImpl healthServiceImpl,
        IOptions<HealthCheckOptions> healthCheckOptions,
        IOptions<GrpcHealthChecksOptions> grpcHealthCheckOptions,
        HealthCheckService healthCheckService,
        IHostApplicationLifetime applicationLifetime)
    {
        _healthCheckOptions = healthCheckOptions.Value;
        _grpcHealthCheckOptions = grpcHealthCheckOptions.Value;
        _healthServiceImpl = healthServiceImpl;
        _healthCheckService = healthCheckService;
        _applicationLifetime = applicationLifetime;
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

    public override async Task Watch(HealthCheckRequest request, IServerStreamWriter<HealthCheckResponse> responseStream, ServerCallContext context)
    {
        ServerCallContext resolvedContext;
        IServerStreamWriter<HealthCheckResponse> resolvedResponseStream;

        if (!_grpcHealthCheckOptions.SuppressCompletionOnShutdown)
        {
            // Create a linked token source to cancel the request if the application is stopping.
            // This is required because the server won't shut down gracefully if the request is still open.
            // The context needs to be wrapped because HealthServiceImpl is in an assembly that can't reference IHostApplicationLifetime.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _applicationLifetime.ApplicationStopping);
            resolvedContext = new WrappedServerCallContext(context, cts);
        }
        else
        {
            resolvedContext = context;
        }

        if (!_grpcHealthCheckOptions.UseHealthChecksCache)
        {
            // Stream writer replaces first health checks results from the cache with newly calculated health check results.
            resolvedResponseStream = new WatchServerStreamWriter(this, request, responseStream, context.CancellationToken);
        }
        else
        {
            resolvedResponseStream = responseStream;
        }

        await _healthServiceImpl.Watch(request, resolvedResponseStream, resolvedContext);

        // If the request is not canceled and the application is stopping then return NotServing before finishing.
        if (!context.CancellationToken.IsCancellationRequested && _applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            await responseStream.WriteAsync(new HealthCheckResponse { Status = HealthCheckResponse.Types.ServingStatus.NotServing });
        }
    }

    private sealed class WrappedServerCallContext : ServerCallContext
    {
        private readonly ServerCallContext _serverCallContext;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public WrappedServerCallContext(ServerCallContext serverCallContext, CancellationTokenSource cancellationTokenSource)
        {
            _serverCallContext = serverCallContext;
            _cancellationTokenSource = cancellationTokenSource;
        }

        protected override string MethodCore => _serverCallContext.Method;
        protected override string HostCore => _serverCallContext.Host;
        protected override string PeerCore => _serverCallContext.Peer;
        protected override DateTime DeadlineCore => _serverCallContext.Deadline;
        protected override Metadata RequestHeadersCore => _serverCallContext.RequestHeaders;
        protected override CancellationToken CancellationTokenCore => _cancellationTokenSource.Token;
        protected override Metadata ResponseTrailersCore => _serverCallContext.ResponseTrailers;
        protected override Status StatusCore
        {
            get => _serverCallContext.Status;
            set => _serverCallContext.Status = value;
        }
        protected override WriteOptions? WriteOptionsCore
        {
            get => _serverCallContext.WriteOptions;
            set => _serverCallContext.WriteOptions = value;
        }
        protected override AuthContext AuthContextCore => _serverCallContext.AuthContext;

        protected override IDictionary<object, object> UserStateCore => _serverCallContext.UserState;

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            return _serverCallContext.CreatePropagationToken(options);
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return _serverCallContext.WriteResponseHeadersAsync(responseHeaders);
        }
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

                if (serviceMapping.HealthCheckPredicate != null && !serviceMapping.HealthCheckPredicate(new HealthCheckMapContext(registration.Name, registration.Tags)))
                {
                    return false;
                }

                return true;
            }, cancellationToken);

            IEnumerable<KeyValuePair<string, HealthReportEntry>> serviceEntries = result.Entries;

#pragma warning disable CS0618 // Type or member is obsolete
            if (serviceMapping.Predicate != null)
            {
                serviceEntries = serviceEntries.Where(entry =>
                {
                    var result = new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data);
                    return serviceMapping.Predicate(result);
                });
            }
#pragma warning restore CS0618 // Type or member is obsolete

            (status, _) = HealthChecksStatusHelpers.GetStatus(serviceEntries);
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
