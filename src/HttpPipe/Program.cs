using Grpc.Net.Client;
using System.Net.Http;
using System;
using System.IO.Pipes;
using CRIRuntime;
using System.Threading.Tasks;

namespace HttpPipe
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var channelOptions = new GrpcChannelOptions
            {
                HttpClient = new HttpClient(new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    ConnectCallback = async (ctx, token) =>
                    {

                        Console.WriteLine("Connecting to pipe server...");
                        var npipeClientStream = new NamedPipeClientStream(".", "containerd-containerd", PipeDirection.InOut, PipeOptions.Asynchronous, 
                            System.Security.Principal.TokenImpersonationLevel.Anonymous);
                        await npipeClientStream.ConnectAsync(5 * 1000, token);

                        var isConnected = npipeClientStream.IsConnected;
                        Console.WriteLine($"pipe connected -- {isConnected}");
                        
                        Console.WriteLine($"There are currently {npipeClientStream.NumberOfServerInstances} pipe server instances open.");
                        Console.WriteLine($"pipe canRead -- {npipeClientStream.CanRead}");
                        Console.WriteLine($"pipe canWrite -- {npipeClientStream.CanWrite}");
                        Console.WriteLine($"pipe opened async -- {npipeClientStream.IsAsync}");
                        Console.WriteLine($"pipe canSeek -- {npipeClientStream.CanSeek}");

                        //var buffer = new byte[4096];
                        //await npipeClientStream.ReadAsync(buffer, 0, 4096);
                        //Console.WriteLine($"read from pipe: {UnicodeEncoding.Default.GetString(buffer)}");

                        //return npipeClientStream;
                        return new ContainerDStream(npipeClientStream);
                    }
                })
            };
            //channelOptions.HttpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            var npipeuri = new Uri("npipe://./pipe/containerd-containerd");
            var scheme = npipeuri.Scheme;
            var segments = npipeuri.Segments;
            var serverName = npipeuri.Host;
            var pipeName = npipeuri.Segments[2];

            var uri = new UriBuilder("http", pipeName).Uri;
            var channel = GrpcChannel.ForAddress(uri, channelOptions);

            var runtimeClient = new RuntimeService.RuntimeServiceClient(channel);

            var listContainersRequest = new ListContainersRequest
            {
                Filter = new ContainerFilter
                {
                    State = new ContainerStateValue { State = ContainerState.ContainerRunning }
                }
            };

            var response = await runtimeClient.ListContainersAsync(listContainersRequest);
            Console.WriteLine($"ListContainers response: continers count -- > {response.Containers.Count}");

            Console.ReadLine();

        }
    }
}
