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
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Ticket;

namespace Sample.Clients
{
    class Program
    {
        private const string Address = "localhost:50051";

        static async Task Main(string[] args)
        {
            var httpClient = new HttpClient { BaseAddress = new Uri($"https://{Address}") };
            var client = GrpcClient.Create<Ticketer.TicketerClient>(httpClient);

            Console.WriteLine("gRPC Ticketer");
            Console.WriteLine();
            Console.WriteLine("Press a key:");
            Console.WriteLine("1: Get available tickets");
            Console.WriteLine("2: Purchase ticket");
            Console.WriteLine("3: Authenticate");
            Console.WriteLine("4: Exit");
            Console.WriteLine();

            string? token = null;

            var exiting = false;
            while (!exiting)
            {
                var consoleKeyInfo = Console.ReadKey(intercept: true);
                switch (consoleKeyInfo.KeyChar)
                {
                    case '1':
                        await GetAvailableTickets(client);
                        break;
                    case '2':
                        await PurchaseTicket(client, token);
                        break;
                    case '3':
                        token = await Authenticate();
                        break;
                    case '4':
                        exiting = true;
                        break;
                }
            }

            Console.WriteLine("Exiting");
        }

        private static async Task<string> Authenticate()
        {
            Console.WriteLine($"Authenticating as {Environment.UserName}...");
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"https://{Address}/generateJwtToken?name={HttpUtility.UrlEncode(Environment.UserName)}"),
                Method = HttpMethod.Get,
                Version = new Version(2, 0)
            };
            var tokenResponse = await httpClient.SendAsync(request);
            tokenResponse.EnsureSuccessStatusCode();

            var token = await tokenResponse.Content.ReadAsStringAsync();
            Console.WriteLine("Successfully authenticated.");

            return token;
        }

        private static async Task PurchaseTicket(Ticketer.TicketerClient client, string? token)
        {
            Console.WriteLine("Purchasing ticket...");
            try
            {
                Metadata? headers = null;
                if (token != null)
                {
                    headers = new Metadata();
                    headers.Add("Authorization", $"Bearer {token}");
                }

                var response = await client.BuyTicketsAsync(new BuyTicketsRequest { Count = 1 }, headers);
                if (response.Success)
                {
                    Console.WriteLine("Purchase successful.");
                }
                else
                {
                    Console.WriteLine("Purchase failed. No tickets available.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error purchasing ticket." + Environment.NewLine + ex.ToString());
            }
        }

        private static async Task GetAvailableTickets(Ticketer.TicketerClient client)
        {
            Console.WriteLine("Getting available ticket count...");
            var response = await client.GetAvailableTicketsAsync(new Empty());
            Console.WriteLine("Available ticket count: " + response.Count);
        }
    }
}
