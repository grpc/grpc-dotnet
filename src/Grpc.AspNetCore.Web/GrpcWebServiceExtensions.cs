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

using System;
using Grpc.AspNetCore.Web;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for the gRPC-Web services.
    /// </summary>
    public static class GrpcWebServiceExtensions
    {
        /// <summary>
        /// Adds gRPC-Web services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="configureOptions">An <see cref="Action{GrpcWebOptions}"/> to configure the provided <see cref="GrpcWebOptions"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddGrpcWeb(this IServiceCollection services, Action<GrpcWebOptions> configureOptions)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return services.Configure(configureOptions);
        }
    }
}
