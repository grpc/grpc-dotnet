﻿#region Copyright notice and license

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

using System;
using Grpc.HealthCheck;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add gRPC service endpoints.
    /// </summary>
    public static class GrpcHealthChecksEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps incoming requests to the gRPC health checks service.
        /// This service can be queried to discover the health of the server.
        /// </summary>
        /// <param name="builder">The <see cref="IEndpointRouteBuilder"/> to add the route to.</param>
        /// <returns>An <see cref="GrpcServiceEndpointConventionBuilder"/> for endpoints associated with the service.</returns>
        public static GrpcServiceEndpointConventionBuilder MapGrpcHealthChecksService(this IEndpointRouteBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.MapGrpcService<HealthServiceImpl>();
        }
    }
}
