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


using Grpc.Core;
using Grpc.Net.Compression;
using System;
using System.Diagnostics;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class ResponseMessageContext
    {
        private ServerCallContext _serverCallContext;

        internal ResponseMessageContext(Type responseType, ServerCallContext serverCallContext)
        {
            ResponseType = responseType;
            _serverCallContext = serverCallContext;
        }

        internal string? GrpcEncoding { get; set; }
        internal Type ResponseType { get; }

        internal bool CanCompress()
        {
            Debug.Assert(
                GrpcEncoding != null,
                "Response encoding should have been calculated at this point.");

            bool compressionEnabled = _serverCallContext.WriteOptions == null || !_serverCallContext.WriteOptions.Flags.HasFlag(WriteFlags.NoCompress);

            return compressionEnabled &&
                !string.Equals(GrpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal);
        }
    }
}
