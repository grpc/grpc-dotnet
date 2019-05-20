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

using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace FunctionalTestsWebsite.Infrastructure
{
    /// <summary>
    /// This endpoint data source can be modified and will raise a change token event.
    /// It can be used to add new endpoints after the application has started.
    /// </summary>
    public class DynamicEndpointDataSource : EndpointDataSource
    {
        private readonly List<Endpoint> _endpoints = new List<Endpoint>();
        private CancellationTokenSource? _cts;
        private CancellationChangeToken? _cct;

        public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

        public override IChangeToken GetChangeToken()
        {
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
            }
            if (_cct == null)
            {
                _cct = new CancellationChangeToken(_cts.Token);
            }

            return _cct;
        }

        public void AddEndpoints(IEnumerable<Endpoint> endpoints)
        {
            _endpoints.AddRange(endpoints);

            if (_cts != null)
            {
                var localCts = _cts;

                _cts = null;
                _cct = null;

                localCts.Cancel();
            }
        }
    }
}
