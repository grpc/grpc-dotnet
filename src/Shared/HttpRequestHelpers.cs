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

using System.Diagnostics.CodeAnalysis;
using System.Net.Http;

namespace Grpc.Shared
{
    internal static class HttpRequestHelpers
    {
        public static bool TryGetOption<T>(this HttpRequestMessage requestMessage, string key, [NotNullWhen(true)] out T? value)
        {
#if NET5_0_OR_GREATER
            return requestMessage.Options.TryGetValue(new HttpRequestOptionsKey<T>(key), out value!);
#else
            if (requestMessage.Properties.TryGetValue(key, out var tempValue))
            {
                value = (T)tempValue!;
                return true;
            }
            value = default;
            return false;
#endif
        }

        public static void SetOption<T>(this HttpRequestMessage requestMessage, string key, T value)
        {
#if NET5_0_OR_GREATER
            requestMessage.Options.Set(new HttpRequestOptionsKey<T>(key), value);
#else
            requestMessage.Properties[key] = value;
#endif
        }
    }
}
