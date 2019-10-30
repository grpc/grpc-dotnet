﻿#region Copyright notice and license

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
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Tests.UnitTests.Helpers
{
    public class TestServerCallContext : ServerCallContext
    {
        private readonly Metadata _requestHeaders;
        private readonly CancellationToken _cancellationToken;
        private readonly Metadata _responseTrailers;
        private readonly AuthContext _authContext;
        private WriteOptions? _writeOptions;
        
        public Metadata? ResponseHeaders { get; private set; }

        private TestServerCallContext(Metadata requestHeaders, CancellationToken cancellationToken)
        {
            _requestHeaders = requestHeaders;
            _cancellationToken = cancellationToken;
            _responseTrailers = new Metadata();
            _authContext = new AuthContext(string.Empty, new Dictionary<string, List<AuthProperty>>());
        }

        protected override string MethodCore => "MethodName";
        protected override string HostCore => "HostName";
        protected override string PeerCore => "PeerName";
        protected override DateTime DeadlineCore { get; }
        protected override Metadata RequestHeadersCore => _requestHeaders;
        protected override CancellationToken CancellationTokenCore => _cancellationToken;
        protected override Metadata ResponseTrailersCore => _responseTrailers;
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get => _writeOptions; set { _writeOptions = value; } }
        protected override AuthContext AuthContextCore => _authContext;

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions options)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            if (ResponseHeaders != null)
            {
                throw new InvalidOperationException("Response headers have already been written.");
            }

            ResponseHeaders = responseHeaders;
            return Task.CompletedTask;
        }

        public static TestServerCallContext Create(Metadata? requestHeaders = null, CancellationToken cancellationToken = default)
        {
            return new TestServerCallContext(requestHeaders ?? new Metadata(), cancellationToken);
        }
    }
}
