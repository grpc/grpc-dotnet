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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Grpc.AspNetCore.Server.Tests.TestObjects;

public class TestHostApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken ApplicationStarted => _cts.Token;
    public CancellationToken ApplicationStopped => _cts.Token;
    public CancellationToken ApplicationStopping => _cts.Token;

    public void StopApplication()
    {
        _cts.Cancel();
    }
}
