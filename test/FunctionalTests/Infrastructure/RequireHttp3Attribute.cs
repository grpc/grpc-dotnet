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
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class RequireHttp3Attribute : NUnitAttribute, IApplyToTest
{
    public void ApplyToTest(NUnit.Framework.Internal.Test test)
    {
        if (test.RunState == RunState.NotRunnable)
        {
            return;
        }

        if (IsSupported(out var message))
        {
            return;
        }

        test.RunState = RunState.Ignored;
        test.Properties.Set(PropertyNames.SkipReason, message!);
    }

    public static bool IsSupported([NotNullWhen(false)] out string? message)
    {
        var osVersion = Environment.OSVersion;
        if (osVersion.Platform == PlatformID.Win32NT &&
            osVersion.Version.Major >= 10 &&
            osVersion.Version.Build >= 22000)
        {
            message = null;
            return true;
        }

        message = "HTTP/3 requires Windows 11 or later.";
        return false;
    }
}
