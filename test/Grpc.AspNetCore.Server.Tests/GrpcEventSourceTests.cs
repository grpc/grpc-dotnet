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
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class GrpcEventSourceTests
    {
        [Test]
        public void MatchesNameAndGuid()
        {
            // Arrange & Act
            var eventSource = new GrpcEventSource();

            // Assert
            Assert.AreEqual("Grpc.AspNetCore.Server", eventSource.Name);
            Assert.AreEqual(Guid.Parse("d442f1b8-5953-548b-3797-d96725947255"), eventSource.Guid);
        }

        [Test]
        public void CallStart()
        {
            // Arrange
            var expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.CallStart("service/method");

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("CallStart", eventData.EventName);
            Assert.AreEqual(EventLevel.Verbose, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual("service/method", eventData.Payload![0]);
        }

        [Test]
        public void CallStop()
        {
            // Arrange
            var expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.CallStop();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("CallStop", eventData.EventName);
            Assert.AreEqual(EventLevel.Verbose, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual(0, eventData.Payload!.Count);
        }

        [Test]
        public void CallFailed()
        {
            // Arrange
            var expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.CallFailed(StatusCode.DeadlineExceeded);

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("CallFailed", eventData.EventName);
            Assert.AreEqual(EventLevel.Error, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual(4, eventData.Payload![0]);
        }

        [Test]
        public void CallDeadlineExceeded()
        {
            // Arrange
            var expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.CallDeadlineExceeded();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("CallDeadlineExceeded", eventData.EventName);
            Assert.AreEqual(EventLevel.Error, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual(0, eventData.Payload!.Count);
        }

        [Test]
        public void MessageSent()
        {
            // Arrange
            var expectedEventId = 5;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.MessageSent();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("MessageSent", eventData.EventName);
            Assert.AreEqual(EventLevel.Verbose, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual(0, eventData.Payload!.Count);
        }

        [Test]
        public void MessageReceived()
        {
            // Arrange
            var expectedEventId = 6;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.MessageReceived();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("MessageReceived", eventData.EventName);
            Assert.AreEqual(EventLevel.Verbose, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual(0, eventData.Payload!.Count);
        }

        [Test]
        public void CallUnimplemented()
        {
            // Arrange
            var expectedEventId = 7;
            var eventListener = new TestEventListener(expectedEventId);
            var grpcEventSource = GetGrpcEventSource();
            eventListener.EnableEvents(grpcEventSource, EventLevel.Verbose);

            // Act
            grpcEventSource.CallUnimplemented("service/method");

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("CallUnimplemented", eventData.EventName);
            Assert.AreEqual(EventLevel.Verbose, eventData.Level);
            Assert.AreSame(grpcEventSource, eventData.EventSource);
            Assert.AreEqual("service/method", eventData.Payload![0]);
        }

        private static GrpcEventSource GetGrpcEventSource()
        {
            return new GrpcEventSource(Guid.NewGuid().ToString());
        }

        private class TestEventListener : EventListener
        {
            private readonly int _eventId;

            public TestEventListener(int eventId)
            {
                _eventId = eventId;
            }

            public EventWrittenEventArgs? EventData { get; private set; }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                // The tests here run in parallel, capture the EventData that a test is explicitly
                // looking for and not give back other tests' data.
                if (eventData.EventId == _eventId)
                {
                    EventData = eventData;
                }
            }
        }
    }
}
