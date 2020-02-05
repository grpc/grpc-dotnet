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

using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Blazor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Weather;

namespace Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("app");

            // Blazor WA currently has an issue related to server streaming. No messages are returned from the server until the call is complete.
            // Setting WasmHttpMessageHandler.StreamingEnabled to true (reflection required) allows server streaming to work - https://github.com/mono/mono/issues/18718
            var wasmHttpMessageHandlerType = System.Reflection.Assembly.Load("WebAssembly.Net.Http").GetType("WebAssembly.Net.Http.HttpClient.WasmHttpMessageHandler");
            var streamingProperty = wasmHttpMessageHandlerType.GetProperty("StreamingEnabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            streamingProperty.SetValue(null, true, null);

            builder.Services.AddSingleton(services =>
            {
                // Create a gRPC-Web channel pointing to the backend server.
                //
                // GrpcWebText is used because server streaming requires it. If server streaming is not used in your app
                // then GrpcWeb is recommended because it produces smaller messages.
                var httpClient = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler()));

                var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions { HttpClient = httpClient });

                return channel;
            });

            await builder.Build().RunAsync();
        }
    }
}
