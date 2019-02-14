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

using System.Threading;
using Microsoft.AspNetCore.Http.Features;

namespace FunctionalTestsWebsite.Infrastructure
{
    /// <summary>
    /// Workaround for https://github.com/aspnet/AspNetCore/issues/7449
    /// </summary>
    public class TestHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
    {
        private CancellationTokenSource _abortableCts = new CancellationTokenSource();
        private CancellationTokenSource _linkedCts;

        public CancellationToken RequestAborted
        {
            get => _linkedCts?.Token ?? _abortableCts.Token;
            set
            {
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_abortableCts.Token, value);
            }
        }

        public void Abort()
        {
            _abortableCts.Cancel();
        }
    }
}
