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

namespace Grpc.Dotnet.Cli.Options;

internal static class CommonOptions
{
    public static Option<string> ProjectOption()
    {
        var o = new Option<string>("--project", ["-p"])
        {
            Description = CoreStrings.ProjectOptionDescription
        };
        return o;
    }

    public static Option<Services> ServiceOption()
    {
        var o = new Option<Services>("--services", ["-s"])
        {
            Description = CoreStrings.ServiceOptionDescription
        };
        return o;
    }

    public static Option<Access> AccessOption()
    {
        var o = new Option<Access>("--access")
        {
            Description = CoreStrings.AccessOptionDescription
        };
        return o;
    }

    public static Option<string> AdditionalImportDirsOption()
    {
        var o = new Option<string>("--additional-import-dirs", ["-i"])
        {
            Description = CoreStrings.AdditionalImportDirsOption
        };
        return o;
    }
}
