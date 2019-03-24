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

using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Grpc.NetCore.HttpClient
{
    internal class PipeContent : HttpContent
    {
        private Pipe _pipe = new Pipe();

        public PipeWriter PipeWriter => _pipe.Writer;
        private PipeReader PipeReader => _pipe.Reader;

        public PipeContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            while (true)
            {
                var result = await PipeReader.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        throw new TaskCanceledException();
                    }

                    if (!buffer.IsEmpty)
                    {
                        var data = buffer.ToArray();
                        stream.Write(data, 0, data.Length);
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    PipeReader.AdvanceTo(buffer.End);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
