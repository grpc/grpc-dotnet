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

using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Grpc.Dotnet.Cli.Extensions
{
    internal static class HttpClientExtensions
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public static Task DownloadFileAsync(string url, string destination) => DownloadFileAsync(url, destination, false);

        public static async Task DownloadFileAsync(string url, string destination, bool overwrite)
        {
            if (!overwrite && File.Exists(destination))
            {
                return;
            }

            using (var stream = await HttpClient.GetStreamAsync(url))
            {
                var desitnationDirectory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(desitnationDirectory))
                {
                    Directory.CreateDirectory(desitnationDirectory);
                }

                using (var fileStream = File.Open(destination, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
        }
    }
}
