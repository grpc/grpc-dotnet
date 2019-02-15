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
using System.IO;

namespace InteropTestsWebsite
{
    public static class Resources
    {
        public static string CertDir = Path.Combine(GetSolutionDirectory(), "testassets/InteropTestsWebsite", "Certs");
        public static string ServerPFXPath = Path.Combine(CertDir, "server1.pfx");

        private static string GetSolutionDirectory()
        {
            var applicationBasePath = AppContext.BaseDirectory;

            var directoryInfo = new DirectoryInfo(applicationBasePath);
            do
            {
                var solutionFileInfo = new FileInfo(Path.Combine(directoryInfo.FullName, "Grpc.AspNetCore.sln"));
                if (solutionFileInfo.Exists)
                {
                    return directoryInfo.FullName;
                }

                directoryInfo = directoryInfo.Parent;
            } while (directoryInfo.Parent != null);

            throw new InvalidOperationException($"Solution directory could not be found for {applicationBasePath}.");
        }
    }
}
