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


namespace Grpc.Dotnet.Cli.Internal;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class GrpcDependencyAttribute : Attribute
{
    public GrpcDependencyAttribute(string name, string version, string privateAssets, string applicableServices, string? applicableToWeb = null)
    {
        Name = name;
        Version = version;
        PrivateAssets = privateAssets;
        ApplicableServices = applicableServices;
        ApplicableToWeb = applicableToWeb;
    }

    public string Name { get; }
    public string Version { get; }
    public string PrivateAssets { get; }
    public string ApplicableServices { get; }
    public string? ApplicableToWeb { get; }
}
