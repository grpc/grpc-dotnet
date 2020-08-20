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

using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Web.Internal
{
    internal static class GrpcWebProtocolConstants
    {
        internal const string GrpcContentType = "application/grpc";
        internal const string GrpcWebContentType = "application/grpc-web";
        internal const string GrpcWebTextContentType = "application/grpc-web-text";

#if NET5_0
        internal static readonly string Http2Protocol = HttpProtocol.Http2;
#else
        internal const string Http2Protocol = "HTTP/2";
#endif
    }
}
