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
using System.Diagnostics;

namespace Grpc.Tests.Shared
{
    public class ActivityReplacer : IDisposable
    {
        private readonly Activity _activity;

        public ActivityReplacer(string activityName)
        {
            _activity = new Activity(activityName);
            _activity.Start();
        }

        public void Dispose()
        {
            Debug.Assert(Activity.Current == _activity);
            _activity.Stop();
        }
    }
}
