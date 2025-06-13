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
using Grpc.Shared.Server;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.Internal;

internal sealed class GrpcServiceOptionsSetup : IConfigureOptions<GrpcServiceOptions>
{
    public void Configure(GrpcServiceOptions options)
    {
        if (!options.MaxReceiveMessageSizeSpecified)
        {
            // Only default MaxReceiveMessageSize if it was not configured
            options._maxReceiveMessageSize = MethodOptions.DefaultReceiveMaxMessageSize;
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
        // Copy internal fields to avoid running logic in property setters.
        options._maxReceiveMessageSize = _options._maxReceiveMessageSize;
        options._maxReceiveMessageSizeSpecified = _options._maxReceiveMessageSizeSpecified;
        options._maxSendMessageSize = _options._maxSendMessageSize;
        options._maxSendMessageSizeSpecified = _options._maxSendMessageSizeSpecified;

        options.EnableDetailedErrors = _options.EnableDetailedErrors;
        options.ResponseCompressionAlgorithm = _options.ResponseCompressionAlgorithm;
        options.ResponseCompressionLevel = _options.ResponseCompressionLevel;
        options.CompressionProviders = _options.CompressionProviders;
        options.IgnoreUnknownServices = _options.IgnoreUnknownServices;
    }
}
