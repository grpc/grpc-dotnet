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

using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Ticket;

namespace GRPCServer
{
    public class TicketerService : Ticketer.TicketerBase
    {
        private readonly TicketRepository _ticketRepository;

        public TicketerService(TicketRepository ticketRepository)
        {
            _ticketRepository = ticketRepository;
        }

        public override Task<AvailableTicketsResponse> GetAvailableTickets(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new AvailableTicketsResponse
            {
                Count = _ticketRepository.GetAvailableTickets()
            });
        }

        [Authorize]
        public override Task<BuyTicketsResponse> BuyTickets(BuyTicketsRequest request, ServerCallContext context)
        {
            var user = context.GetHttpContext().User;

            return Task.FromResult(new BuyTicketsResponse
            {
                Success = _ticketRepository.BuyTickets(user.Identity.Name, request.Count)
            });
        }
    }
}
