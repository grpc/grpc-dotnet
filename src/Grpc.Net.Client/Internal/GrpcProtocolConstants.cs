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
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;

namespace Grpc.Net.Client.Internal
{
    internal static class GrpcProtocolConstants
    {
        internal const string GrpcContentType = "application/grpc";
        internal static readonly MediaTypeHeaderValue GrpcContentTypeHeaderValue = new MediaTypeHeaderValue("application/grpc");

        internal const string TimeoutHeader = "grpc-timeout";
        internal const string MessageEncodingHeader = "grpc-encoding";

        internal const string StatusTrailer = "grpc-status";
        internal const string MessageTrailer = "grpc-message";

        internal const string MessageAcceptEncodingHeader = "grpc-accept-encoding";

        internal static readonly ProductInfoHeaderValue UserAgentHeader;
        internal static readonly TransferCodingWithQualityHeaderValue TEHeader;

        static GrpcProtocolConstants()
        {
            var userAgent = "grpc-dotnet";

            var assemblyVersion = typeof(GrpcProtocolConstants)
                .Assembly
                .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault();

            Debug.Assert(assemblyVersion != null);

            // assembly version attribute should always be present
            // but in case it isn't then don't include version in user-agent
            if (assemblyVersion != null)
            {
                userAgent += "/" + assemblyVersion.InformationalVersion;
            }

            UserAgentHeader = ProductInfoHeaderValue.Parse(userAgent);

            TEHeader = new TransferCodingWithQualityHeaderValue("trailers");
        }
    }
}
