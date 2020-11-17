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
using System.Net.Http.Headers;

namespace Grpc.Net.Client.Web.Internal
{
    internal static class GrpcWebProtocolConstants
    {
#if !NETSTANDARD2_0
        public static readonly Version Http2Version = System.Net.HttpVersion.Version20;
#else
        public static readonly Version Http2Version = new Version(2, 0);
#endif

        public const string GrpcContentType = "application/grpc";
        public const string GrpcWebContentType = "application/grpc-web";
        public const string GrpcWebTextContentType = "application/grpc-web-text";

        public static readonly MediaTypeHeaderValue GrpcHeader = new MediaTypeHeaderValue(GrpcContentType);
        public static readonly MediaTypeHeaderValue GrpcWebHeader = new MediaTypeHeaderValue(GrpcWebContentType);
        public static readonly MediaTypeHeaderValue GrpcWebTextHeader = new MediaTypeHeaderValue(GrpcWebTextContentType);
    }
}
