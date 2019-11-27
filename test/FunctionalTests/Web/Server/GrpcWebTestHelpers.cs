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
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Server
{
    public static class GrpcWebTestHelpers
    {
        public static async Task AssertSuccessTrailerAsync(PipeReader pipeReader)
        {
            var headers = await ParseHeadersAsync(pipeReader);
            Assert.AreEqual("0", headers.GetValues("grpc-status").Single());
        }

        public static async Task<HttpHeaders> ParseHeadersAsync(PipeReader pipeReader)
        {
            var httpHeaders = new TestHttpHeaders();

            var readResult = await pipeReader.ReadAsync();
            if (readResult.Buffer.Length == 0)
            {
                return httpHeaders;
            }

            var isTrailer = IsBitSet(readResult.Buffer.FirstSpan[0], 7);

            Assert.IsTrue(isTrailer);

            var trailerSizeData = readResult.Buffer.Slice(1, 4);
            var trailerSize = BinaryPrimitives.ReadUInt32BigEndian(trailerSizeData.ToArray());

            var trailerData = readResult.Buffer.Slice(5).ToArray();
            Assert.AreEqual(trailerSize, trailerData.Length);

            var trailerString = Encoding.UTF8.GetString(trailerData);

            StringReader sr = new StringReader(trailerString);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    var delimiter = line.IndexOf(':');
                    var name = line.Substring(0, delimiter);
                    var value = line.Substring(delimiter + 1).Trim();

                    httpHeaders.Add(name, value);
                }
            }

            return httpHeaders;
        }

        private static bool IsBitSet(byte b, int pos)
        {
            return ((b >> pos) & 1) != 0;
        }
    }
}
