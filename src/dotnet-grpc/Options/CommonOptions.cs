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

using System.CommandLine;
using Grpc.Dotnet.Cli.Properties;

namespace Grpc.Dotnet.Cli.Options
{
    internal static class CommonOptions
    {
        public static Option ProjectOption()
        {
            var o = new Option(
                aliases: new[] { "-p", "--project" },
                description: CoreStrings.ProjectOptionDescription);
            o.Argument = new Argument<FileInfo> { Name = "project" };
            return o;
        }

        public static Option ServiceOption()
        {
            var o = new Option(
                aliases: new[] { "-s", "--services" },
                description: CoreStrings.ServiceOptionDescription);
            o.Argument = new Argument<Services> { Name = "services" };
            return o;
        }

        public static Option AccessOption()
        {
            var o = new Option(
                aliases: new[] { "--access" },
                description: CoreStrings.AccessOptionDescription);
            o.Argument = new Argument<Access> { Name = "access" };
            return o;
        }

        public static Option AdditionalImportDirsOption()
        {
            var o = new Option(
                aliases: new[] { "-i", "--additional-import-dirs" },
                description: CoreStrings.AdditionalImportDirsOption);
            o.Argument = new Argument<string> { Name = "dirs" };
            return o;
        }
    }
}
