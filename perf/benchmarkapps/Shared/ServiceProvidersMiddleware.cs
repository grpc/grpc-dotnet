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

using Microsoft.AspNetCore.Http.Features;

namespace Grpc.Shared
{
    public class ServiceProvidersMiddleware
    {
        private readonly ServiceProvidersFeature _serviceProvidersFeature;
        private readonly RequestDelegate _next;

        public ServiceProvidersMiddleware(RequestDelegate next, IServiceProvider serviceProvider)
        {
            _serviceProvidersFeature = new ServiceProvidersFeature(serviceProvider);
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            // Configure request to use application services to avoid creating a request scope
            context.Features.Set<IServiceProvidersFeature>(_serviceProvidersFeature);
            return _next(context);
        }

        private class ServiceProvidersFeature : IServiceProvidersFeature
        {
            public ServiceProvidersFeature(IServiceProvider requestServices)
            {
                RequestServices = requestServices;
            }

            public IServiceProvider RequestServices { get; set; }
        }
    }
}
