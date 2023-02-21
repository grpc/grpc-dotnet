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

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal;

internal sealed class GrpcEventSource : EventSource
{
    public static readonly GrpcEventSource Log = new GrpcEventSource();

    private PollingCounter? _totalCallsCounter;
    private PollingCounter? _currentCallsCounter;
    private PollingCounter? _messagesSentCounter;
    private PollingCounter? _messagesReceivedCounter;
    private PollingCounter? _callsFailedCounter;
    private PollingCounter? _callsDeadlineExceededCounter;
    private PollingCounter? _callsUnimplementedCounter;

    private long _totalCalls;
    private long _currentCalls;
    private long _messageSent;
    private long _messageReceived;
    private long _callsFailed;
    private long _callsDeadlineExceeded;
    private long _callsUnimplemented;

    internal GrpcEventSource()
        : base("Grpc.AspNetCore.Server")
    {
    }

    [NonEvent]
    internal void ResetCounters()
    {
        _totalCalls = 0;
        _currentCalls = 0;
        _messageSent = 0;
        _messageReceived = 0;
        _callsFailed = 0;
        _callsDeadlineExceeded = 0;
    }

    // Used for testing
    internal GrpcEventSource(string eventSourceName)
        : base(eventSourceName)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 1, Level = EventLevel.Verbose)]
    public void CallStart(string method)
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _totalCalls);
        Interlocked.Increment(ref _currentCalls);

        WriteEvent(1, method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 2, Level = EventLevel.Verbose)]
    public void CallStop()
    {
        AssertEventSourceEnabled();

        Interlocked.Decrement(ref _currentCalls);

        WriteEvent(2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 3, Level = EventLevel.Error)]
    public void CallFailed(StatusCode statusCode)
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _callsFailed);

        WriteEvent(3, (int)statusCode);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 4, Level = EventLevel.Error)]
    public void CallDeadlineExceeded()
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _callsDeadlineExceeded);

        WriteEvent(4);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 5, Level = EventLevel.Verbose)]
    public void MessageSent()
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _messageSent);

        WriteEvent(5);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 6, Level = EventLevel.Verbose)]
    public void MessageReceived()
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _messageReceived);

        WriteEvent(6);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(eventId: 7, Level = EventLevel.Verbose)]
    public void CallUnimplemented(string method)
    {
        AssertEventSourceEnabled();

        Interlocked.Increment(ref _callsUnimplemented);

        WriteEvent(7, method);
    }

    [Conditional("DEBUG")]
    [NonEvent]
    private void AssertEventSourceEnabled()
    {
        Debug.Assert(IsEnabled(), "Event source should be enabled.");
    }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command == EventCommand.Enable)
        {
            // This is the convention for initializing counters in the RuntimeEventSource (lazily on the first enable command).
            // They aren't disabled afterwards...

            _totalCallsCounter ??= new PollingCounter("total-calls", this, () => Volatile.Read(ref _totalCalls))
            {
                DisplayName = "Total Calls",
            };
            _currentCallsCounter ??= new PollingCounter("current-calls", this, () => Volatile.Read(ref _currentCalls))
            {
                DisplayName = "Current Calls"
            };
            _callsFailedCounter ??= new PollingCounter("calls-failed", this, () => Volatile.Read(ref _callsFailed))
            {
                DisplayName = "Total Calls Failed",
            };
            _callsDeadlineExceededCounter ??= new PollingCounter("calls-deadline-exceeded", this, () => Volatile.Read(ref _callsDeadlineExceeded))
            {
                DisplayName = "Total Calls Deadline Exceeded",
            };
            _messagesSentCounter ??= new PollingCounter("messages-sent", this, () => Volatile.Read(ref _messageSent))
            {
                DisplayName = "Total Messages Sent",
            };
            _messagesReceivedCounter ??= new PollingCounter("messages-received", this, () => Volatile.Read(ref _messageReceived))
            {
                DisplayName = "Total Messages Received",
            };
            _callsUnimplementedCounter ??= new PollingCounter("calls-unimplemented", this, () => Volatile.Read(ref _callsUnimplemented))
            {
                DisplayName = "Total Calls Unimplemented",
            };
        }
    }
}
