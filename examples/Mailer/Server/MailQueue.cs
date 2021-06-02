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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Mail;

namespace Server
{
    public record Mail(int Id, string Content);

    public record MailQueueChangeState(int TotalCount, int ForwardedCount, MailboxMessage.Types.Reason Reason);

    public class MailQueue
    {
        private readonly Channel<Mail> _incomingMail;
        private int _totalMailCount;
        private int _forwardedMailCount;

        public string Name { get; }
        public event Func<MailQueueChangeState, Task>? Changed;

        public MailQueue(string name)
        {
            Name = name;

            _incomingMail = Channel.CreateUnbounded<Mail>();

            _ = Task.Run(async () =>
            {
                var random = new Random();

                while (true)
                {
                    _totalMailCount++;
                    var mail = new Mail(_totalMailCount, $"Message #{_totalMailCount}");
                    await _incomingMail.Writer.WriteAsync(mail);
                    OnChange(MailboxMessage.Types.Reason.Received);

                    await Task.Delay(TimeSpan.FromSeconds(random.Next(5, 15)));
                }
            });
        }

        public bool TryForwardMail([NotNullWhen(true)] out Mail? message)
        {
            if (_incomingMail.Reader.TryRead(out message))
            {
                Interlocked.Increment(ref _forwardedMailCount);
                OnChange(MailboxMessage.Types.Reason.Forwarded);

                return true;
            }

            return false;
        }

        private void OnChange(MailboxMessage.Types.Reason reason)
        {
            Changed?.Invoke(new MailQueueChangeState(_totalMailCount, _forwardedMailCount, reason));
        }
    }
}
