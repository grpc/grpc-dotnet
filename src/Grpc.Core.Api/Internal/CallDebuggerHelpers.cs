#region Copyright notice and license

// Copyright 2015-2016 gRPC authors.
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

namespace Grpc.Core.Internal;

internal static class CallDebuggerHelpers
{
    public static string DebuggerToString(AsyncCallState callState)
    {
        var status = GetStatus(callState);
        var debugText = $"IsComplete = {((status != null) ? "true" : "false")}";
        if (status != null)
        {
            debugText += $", Status = {status}";
        }
        return debugText;
    }

    public static Status? GetStatus(AsyncCallState callState)
    {
        // This is the only public API to get this value and there is no way to check if it's available.
        // The overhead of throwing an error in the background is acceptable because this is only called while debugging.
        try
        {
            return callState.GetStatus();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static Metadata? GetTrailers(AsyncCallState callState)
    {
        // This is the only public API to get this value and there is no way to check if it's available.
        // The overhead of throwing an error in the background is acceptable because this is only called while debugging.
        try
        {
            return callState.GetTrailers();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
