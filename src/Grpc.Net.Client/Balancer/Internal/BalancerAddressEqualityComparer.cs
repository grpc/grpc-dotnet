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
using System.Diagnostics.CodeAnalysis;

namespace Grpc.Net.Client.Balancer.Internal;

internal sealed class BalancerAddressEqualityComparer : IEqualityComparer<BalancerAddress>
{
    internal static readonly BalancerAddressEqualityComparer Instance = new BalancerAddressEqualityComparer();

    public bool Equals(BalancerAddress? x, BalancerAddress? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        if (!x.EndPoint.Equals(y.EndPoint))
        {
            return false;
        }

        return BalancerAttributes.DeepEquals(x._attributes, y._attributes);
    }

    public int GetHashCode([DisallowNull] BalancerAddress obj)
    {
        throw new NotSupportedException();
    }
}
#endif
