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


namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class TestDelegateHandler : DelegatingHandler
    {
        public TestDelegateHandler(Action<HttpRequestMessage>? requestAction = null, Action<HttpResponseMessage>? responseAction = null)
        {
            RequestAction = requestAction;
            ResponseAction = responseAction;
        }

        public Action<HttpRequestMessage>? RequestAction { get; }
        public Action<HttpResponseMessage>? ResponseAction { get; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestAction?.Invoke(request);
            var response = await base.SendAsync(request, cancellationToken);
            ResponseAction?.Invoke(response);

            return response;
        }
    }
}
