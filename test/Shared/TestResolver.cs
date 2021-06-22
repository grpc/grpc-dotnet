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

namespace Grpc.Tests.Shared
{
    internal class TestResolver : Resolver
    {
        private readonly Func<Task>? _onRefreshAsync;
        private ResolverResult? _result;
        private Action<ResolverResult>? _listener;

        public TestResolver(Func<Task>? onRefreshAsync = null)
        {
            _onRefreshAsync = onRefreshAsync;
        }

        public void UpdateEndPoints(List<DnsEndPoint> endPoints, ServiceConfig? serviceConfig = null)
        {
            UpdateResult(ResolverResult.ForResult(endPoints, serviceConfig));
        }

        public void UpdateError(Status status)
        {
            UpdateResult(ResolverResult.ForFailure(status));
        }

        public void UpdateResult(ResolverResult result)
        {
            _result = result;
            _listener?.Invoke(result);
        }

        protected override void Dispose(bool disposing)
        {
            _listener = null;
        }

        public override Task RefreshAsync(CancellationToken cancellationToken)
        {
            _listener?.Invoke(_result ?? ResolverResult.ForResult(Array.Empty<DnsEndPoint>(), serviceConfig: null));
            return _onRefreshAsync?.Invoke() ?? Task.CompletedTask;
        }

        public override void Start(Action<ResolverResult> listener)
        {
            _listener = listener;
        }
    }
}
#endif
