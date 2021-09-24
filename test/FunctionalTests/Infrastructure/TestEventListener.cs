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
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    /// <summary>
    /// An eventer listener than listens to counter updates and provides a way to subscribe to expected values.
    /// </summary>
    public class TestEventListener : EventListener
    {
        private readonly object _lock = new object();
        private readonly List<ListenerSubscription> _subscriptions;
        private readonly ILogger _logger;
        private readonly int _eventId;
        private readonly EventSource _eventSource;

        public TestEventListener(int eventId, ILoggerFactory loggerFactory, EventSource eventSource)
        {
            _eventId = eventId;
            _eventSource = eventSource;
            _subscriptions = new List<ListenerSubscription>();
            _logger = loggerFactory.CreateLogger<TestEventListener>();
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource != _eventSource)
            {
                return;
            }

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
                            subscription.CheckCount++;
                            var currentValue = Convert.ToInt64(value);

                            if (subscription.ExpectedValue == currentValue)
                            {
                                _logger.LogDebug($"Check {subscription.CheckCount}: {subscription.CounterName} current value {currentValue} matched expected {subscription.ExpectedValue}.");

                                subscription.SetMatched();
                                subscription.Dispose();
                            }
                            else
                            {
                                if (!subscription.IsMatched)
                                {
                                    if (subscription.LastValue != currentValue || subscription.CheckCount % 1000 == 0)
                                    {
                                        _logger.LogDebug($"Check {subscription.CheckCount}: {subscription.CounterName} current value {currentValue} doesn't match expected {subscription.ExpectedValue}.");
                                    }
                                }
                            }

                            // For debugging. Printed in message if subscription fails.
                            subscription.LastValue = currentValue;
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
