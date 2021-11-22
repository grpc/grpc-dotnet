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

using Grpc.AspNetCore.Web;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// gRPC-Web extension methods for <see cref="IEndpointConventionBuilder"/>.
    /// </summary>
    public static class GrpcWebEndpointConventionBuilderExtensions
    {
        /// <summary>
        /// Enables gRPC-Web for the endpoint(s).
        /// </summary>
        /// <param name="builder">The endpoint convention builder.</param>
        /// <returns>The original convention builder parameter.</returns>
        public static TBuilder EnableGrpcWeb<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Add(endpointBuilder =>
            {
                endpointBuilder.Metadata.Add(new EnableGrpcWebAttribute());
            });

            return builder;
        }

        /// <summary>
        /// Disable gRPC-Web for the endpoint(s).
        /// </summary>
        /// <param name="builder">The endpoint convention builder.</param>
        /// <returns>The original convention builder parameter.</returns>
        public static TBuilder DisableGrpcWeb<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Add(endpointBuilder =>
            {
                endpointBuilder.Metadata.Add(new DisableGrpcWebAttribute());
            });

            return builder;
        }
    }
}
