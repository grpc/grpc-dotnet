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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Grpc.AspNetCore.Server.Tests.Infrastructure
{
    public class TestHttpResponseFeature : IHttpResponseFeature
    {
        public List<(Func<object, Task> callback, object state)> StartingCallbacks { get; }
            = new List<(Func<object, Task> callback, object state)>();

        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; } = string.Empty;
        public int StatusCode { get; set; }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            StartingCallbacks.Add((callback, state));
        }

        public int StartingCallbackCount => StartingCallbacks.Count;
    }
}
