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

using System;
using System.CommandLine;
using System.Globalization;

namespace Grpc.Dotnet.Cli.Internal
{
    internal static class ConsoleExtensions
    {
        public static void Log(this IConsole console, string formatString, params string[] args)
        {
            console.Out.WriteLine(string.Format(CultureInfo.CurrentCulture, formatString, args));
        }

        public static void LogWarning(this IConsole console, string formatString, params string[] args)
        {
            console.Out.WriteLine(string.Format(CultureInfo.CurrentCulture, $"Warning: {formatString}", args));
        }

        public static void LogError(this IConsole console, Exception e)
        {
            console.Error.WriteLine($"Error: {e.Message}");
        }
    }
}
