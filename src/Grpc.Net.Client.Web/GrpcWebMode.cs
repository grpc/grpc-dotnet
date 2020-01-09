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

namespace Grpc.Net.Client.Web
{
    /// <summary>
    /// The gRPC-Web mode.
    /// </summary>
    public enum GrpcWebMode
    {
        /// <summary>
        /// Calls are made using the <c>grpc-web</c> content type. Request content is not translated to base64.
        /// </summary>
        GrpcWeb,
        /// <summary>
        /// Calls are made using the <c>grpc-web-text</c> content type. Request content is translated to base64.
        /// </summary>
        GrpcWebText
    }
}
