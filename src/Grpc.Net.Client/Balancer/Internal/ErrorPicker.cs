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

#if SUPPORT_LOAD_BALANCING
using System.Diagnostics;
using Grpc.Core;

namespace Grpc.Net.Client.Balancer.Internal;

internal sealed class ErrorPicker : SubchannelPicker
{
    private readonly Status _status;

    public ErrorPicker(Status status)
    {
        Debug.Assert(status.StatusCode != StatusCode.OK, "Error status code must not be OK.");
        _status = status;
    }

    public override PickResult Pick(PickContext context)
    {
        return PickResult.ForFailure(_status);
    }
}
#endif
