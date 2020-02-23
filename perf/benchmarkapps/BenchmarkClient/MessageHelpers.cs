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

using Google.Protobuf;
using Grpc.Testing;

namespace BenchmarkClient
{
    public static class MessageHelpers
    {
        public static readonly int ResponseMessageSize = 10;
        public static readonly int RequestMessageSize = 10;

        public static SimpleRequest CreateRequestMessage()
        {
            var message = new SimpleRequest
            {
                ResponseSize = ResponseMessageSize
            };
            if (RequestMessageSize > 0)
            {
                message.Payload = new Payload();
                message.Payload.Body = ByteString.CopyFrom(new byte[RequestMessageSize]);
            }

            return message;
        }
    }
}