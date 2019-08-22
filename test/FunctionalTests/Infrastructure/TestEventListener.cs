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
using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    /// <summary>
    /// An eventer listener than listens to counter updates and provides a way to subscribe to expected values.
    /// </summary>
    public class TestEventListener : EventListener
    {
        private readonly object _lock = new object();
        private readonly List<ListenerSubscription> _subscriptions;

        private readonly int _eventId;

        public TestEventListener(int eventId)
        {
            _eventId = eventId;
            _subscriptions = new List<ListenerSubscription>();
        }

        public EventWrittenEventArgs? EventData { get; private set; }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Subscriptions change on multiple threads so make a local copy
            ListenerSubscription[]? subscriptions = null;
            lock (_lock)
            {
                // Somehow OnEventWritten is being called when _subscriptions is null.
                // I don't know how/why but if it is null then we can just exit the method.
                subscriptions = _subscriptions?.ToArray();
            }

            if (subscriptions == null || subscriptions.Length == 0)
            {
                return;
            }

            // The tests here run in parallel, capture the EventData that a test is explicitly
            // looking for and not give back other tests' data.
            if (eventData.EventId == _eventId && eventData.Payload != null)
            {
                var eventPayload = (IDictionary<string, object?>)eventData.Payload[0]!;
                if (eventPayload.TryGetValue("Name", out var name) &&
                    eventPayload.TryGetValue("Mean", out var value))
                {
                    foreach (var subscription in subscriptions)
                    {
                        if (subscription.CounterName == Convert.ToString(name))
                        {
                            var currentValue = Convert.ToInt64(value);

                            // For debugging. Printed in message if subscription fails.
                            subscription.LastValue = currentValue;

                            if (subscription.ExpectedValue == currentValue)
                            {
                                subscription.SetMatched();
                                subscription.Dispose();
                            }
                        }
                    }
                }
            }
        }

        public ListenerSubscription Subscribe(string counterName, long expectedValue)
        {
            var subscription = new ListenerSubscription(this, counterName, expectedValue);
            lock (_lock)
            {
                _subscriptions.Add(subscription);
            }

            return subscription;
        }

        public void Unsubscribe(ListenerSubscription listenerSubscription)
        {
            lock (_lock)
            {
                _subscriptions.Remove(listenerSubscription);
            }
        }
    }
}
