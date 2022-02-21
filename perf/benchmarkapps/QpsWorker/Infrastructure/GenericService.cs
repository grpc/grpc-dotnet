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

namespace QpsWorker.Infrastructure
{
    public class GenericService
    {
        private readonly static Marshaller<byte[]> ByteArrayMarshaller = new Marshaller<byte[]>((b) => b, (b) => b);

        public readonly static Method<byte[], byte[]> StreamingCallMethod = new Method<byte[], byte[]>(
            MethodType.DuplexStreaming,
            "grpc.testing.BenchmarkService",
            "StreamingCall",
            ByteArrayMarshaller,
            ByteArrayMarshaller
        );

        public static async Task DuplexStreamingServerMethod(
            GenericService service,
            IAsyncStreamReader<byte[]> requestStream,
            IServerStreamWriter<byte[]> responseStream,
            ServerCallContext serverCallContext)
        {
            await foreach (var request in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(service.Response);
            }
        }

        public byte[] Response { get; }

        public GenericService(int responseSize)
        {
            Response = new byte[responseSize];
        }
    }
}
