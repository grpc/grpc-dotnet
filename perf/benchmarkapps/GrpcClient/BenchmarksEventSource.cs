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

using System.Diagnostics.Tracing;

namespace GrpcClient
{
    internal sealed class BenchmarksEventSource : EventSource
    {
        public static readonly BenchmarksEventSource Log = new BenchmarksEventSource();

        internal BenchmarksEventSource()
            : this("Benchmarks")
        {

        }

        // Used for testing
        internal BenchmarksEventSource(string eventSourceName)
            : base(eventSourceName)
        {
        }

        [Event(1, Level = EventLevel.Informational)]
        public void Measure(string name, long value)
        {
            WriteEvent(1, name, value);
        }

        public static void Measure(string name, double value)
        {
            Log.MeasureDouble(name, value);
        }

        public static void Measure(string name, string value)
        {
            Log.MeasureString(name, value);
        }

        [Event(2, Level = EventLevel.Informational)]
        public void MeasureDouble(string name, double value)
        {
            WriteEvent(2, name, value);
        }

        [Event(3, Level = EventLevel.Informational)]
        public void MeasureString(string name, string value)
        {
            WriteEvent(3, name, value);
        }

        [Event(5, Level = EventLevel.Informational)]
        public void Metadata(string name, string aggregate, string reduce, string shortDescription, string longDescription, string format)
        {
            WriteEvent(5, name, aggregate, reduce, shortDescription, longDescription, format);
        }
    }
}
