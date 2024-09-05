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

using Microsoft.Extensions.Logging;

namespace GrpcClient;

public class ClientOptions
{
    public Uri? Url { get; set; }
    public string? UdsFileName { get; set; }
    public string? NamedPipeName { get; set; }
    public int Connections { get; set; }
    public int Warmup { get; set; }
    public int Duration { get; set; }
    public int? CallCount { get; set; }
    public string? Scenario { get; set; }
    public bool Latency { get; set; }
    public string? Protocol { get; set; }
    public bool EnableCertAuth { get; set; }
    public LogLevel LogLevel { get; set; }
    public int RequestSize { get; set; }
    public int ResponseSize { get; set; }
    public GrpcClientType GrpcClientType { get; set; }
    public int Streams { get; set; }
    public int Deadline { get; set; }
}
