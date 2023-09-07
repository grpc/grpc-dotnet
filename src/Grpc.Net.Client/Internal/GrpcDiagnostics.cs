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

using System.Diagnostics;

namespace Grpc.Net.Client.Internal;

internal static class GrpcDiagnostics
{
    // These are static on a non-generic class. We don't want these re-created for each type argument.
    public static readonly DiagnosticListener DiagnosticListener = new DiagnosticListener("Grpc.Net.Client");
    public static readonly ActivitySource ActivitySource = new ActivitySource("Grpc.Net.Client");

    public const string ActivityName = "Grpc.Net.Client.GrpcOut";

    public const string ActivityStartKey = ActivityName + ".Start";
    public const string ActivityStopKey = ActivityName + ".Stop";

    public const string GrpcMethodTagName = "grpc.method";
    public const string GrpcStatusCodeTagName = "grpc.status_code";
}
