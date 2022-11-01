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


namespace Grpc.Net.Client.Internal;

internal static class RuntimeHelpers
{
    public static bool QueryRuntimeSettingSwitch(string switchName, bool defaultValue)
    {
        if (AppContext.TryGetSwitch(switchName, out var value))
        {
            return value;
        }

        return defaultValue;
    }
}
