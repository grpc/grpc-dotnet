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

namespace Grpc.Shared
{
    internal enum Http3ErrorCode : long
    {
        H3_NO_ERROR = 0x100,
        H3_GENERAL_PROTOCOL_ERROR = 0x101,
        H3_INTERNAL_ERROR = 0x102,
        H3_STREAM_CREATION_ERROR = 0x103,
        H3_CLOSED_CRITICAL_STREAM = 0x104,
        H3_FRAME_UNEXPECTED = 0x105,
        H3_FRAME_ERROR = 0x106,
        H3_EXCESSIVE_LOAD = 0x107,
        H3_ID_ERROR = 0x108,
        H3_SETTINGS_ERROR = 0x109,
        H3_MISSING_SETTINGS = 0x10a,
        H3_REQUEST_REJECTED = 0x10b,
        H3_REQUEST_CANCELLED = 0x10c,
        H3_REQUEST_INCOMPLETE = 0x10d,
        H3_MESSAGE_ERROR = 0x10e,
        H3_CONNECT_ERROR = 0x10f,
        H3_VERSION_FALLBACK = 0x110,
    }
}
