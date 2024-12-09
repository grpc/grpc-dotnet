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

using System.IO.Compression;
using Grpc.Net.Compression;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.Internal;

internal sealed class GrpcServiceOptionsSetup : IConfigureOptions<GrpcServiceOptions>
{
    // Default to no send limit and 4mb receive limit.
    // Matches the gRPC C impl defaults
    // https://github.com/grpc/grpc/blob/977df7208a6e3f9a62a6369af5cd6e4b69b4fdec/include/grpc/impl/codegen/grpc_types.h#L413-L416
    internal const int DefaultReceiveMaxMessageSize = 4 * 1024 * 1024;

    public void Configure(GrpcServiceOptions options)
    {
        if (!options._maxReceiveMessageSizeConfigured)
        {
            // Only default MaxReceiveMessageSize if it was not configured
            options._maxReceiveMessageSize = DefaultReceiveMaxMessageSize;
        }
        if (options._compressionProviders == null || options._compressionProviders.Count == 0)
        {
            options.CompressionProviders.Add(new GzipCompressionProvider(CompressionLevel.Fastest));

            options.CompressionProviders.Add(new DeflateCompressionProvider(CompressionLevel.Fastest));
        }
    }
}

internal sealed class GrpcServiceOptionsSetup<TService> : IConfigureOptions<GrpcServiceOptions<TService>> where TService : class
{
    private readonly GrpcServiceOptions _options;

    public GrpcServiceOptionsSetup(IOptions<GrpcServiceOptions> options)
    {
        _options = options.Value;
    }

    public void Configure(GrpcServiceOptions<TService> options)
    {
        options.MaxReceiveMessageSize = _options.MaxReceiveMessageSize;
        options.MaxSendMessageSize = _options.MaxSendMessageSize;
        options.EnableDetailedErrors = _options.EnableDetailedErrors;
        options.ResponseCompressionAlgorithm = _options.ResponseCompressionAlgorithm;
        options.ResponseCompressionLevel = _options.ResponseCompressionLevel;
        options.CompressionProviders = _options.CompressionProviders;
        options.IgnoreUnknownServices = _options.IgnoreUnknownServices;
    }
}
