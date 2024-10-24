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

using System.Collections;
using Grpc.Core;

namespace Grpc.Net.Client.Internal;

internal sealed class GrpcCallScope : IReadOnlyList<KeyValuePair<string, object>>
{
    private const string GrpcMethodTypeKey = "GrpcMethodType";
    private const string GrpcUriKey = "GrpcUri";

    private readonly MethodType _methodType;
    private readonly Uri _uri;
    private string? _cachedToString;

    public GrpcCallScope(MethodType methodType, Uri uri)
    {
        _methodType = methodType;
        _uri = uri;
    }

    public KeyValuePair<string, object> this[int index]
    {
        get
        {
            switch (index)
            {
                case 0:
                    return new KeyValuePair<string, object>(GrpcMethodTypeKey, _methodType);
                case 1:
                    return new KeyValuePair<string, object>(GrpcUriKey, _uri);
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public int Count => 2;

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (var i = 0; i < Count; ++i)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        if (_cachedToString == null)
        {
            _cachedToString = FormattableString.Invariant($"{GrpcMethodTypeKey}:{_methodType} {GrpcUriKey}:{_uri}");
        }

        return _cachedToString;
    }
}
