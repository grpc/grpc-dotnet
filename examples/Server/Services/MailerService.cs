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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Mail;
using Microsoft.Extensions.Logging;

namespace GRPCServer
{
    public class MailerService : Mailer.MailerBase
    {
        private readonly ILogger _logger;
        private readonly MailQueueRepository _messageQueueRepository;

        public MailerService(ILoggerFactory loggerFactory, MailQueueRepository messageQueueRepository)
        {
            _logger = loggerFactory.CreateLogger<MailerService>();
            _messageQueueRepository = messageQueueRepository;
        }

        public async override Task Mailbox(
            IAsyncStreamReader<ForwardMailMessage> requestStream,
            IServerStreamWriter<MailboxMessage> responseStream,
            ServerCallContext context)
        {
            var mailboxName = context.RequestHeaders.Single(e => e.Key == "mailbox-name").Value;

            var mailQueue = _messageQueueRepository.GetMailQueue(mailboxName);

            _logger.LogInformation($"Connected to {mailboxName}");

            mailQueue.Changed += ReportChanges;

            try
            {
                while (await requestStream.MoveNext())
                {
                    if (mailQueue.TryForwardMail(out var message))
                    {
                        _logger.LogInformation($"Forwarded mail: {message.Content}");
                    }
                    else
                    {
                        _logger.LogWarning("No mail to forward.");
                    }
                }
            }
            finally
            {
                mailQueue.Changed -= ReportChanges;
            }

            _logger.LogInformation($"{mailboxName} disconnected");

            async Task ReportChanges((int totalCount, int fowardCount, MailboxMessage.Types.Reason reason) state)
            {
                await responseStream.WriteAsync(new MailboxMessage
                {
                    Forwarded = state.fowardCount,
                    New = state.totalCount - state.fowardCount,
                    Reason = state.reason
                });
            }
        }
    }
}
