using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.Net.ClientFactory.Tests;

public static class CustomHttpHandlerTests
{
    private static readonly MethodInfo _CallInvokerGetter =
        typeof(ClientBase)
            .GetProperty("CallInvoker", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetMethod
        ?? throw new InvalidOperationException("!!!");
    private static readonly MethodInfo _HandlerTypeGetter =
        typeof(GrpcChannel)
            .GetProperty("HttpHandlerType", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetMethod
        ?? throw new InvalidOperationException("!!!");

    [Test]
    public static async Task TestMany()
    {
        for (int i = 0; i < 1000; i++)
        {
            await DoTestAsync();
        }
    }

    private static async Task DoTestAsync()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddGrpcClient<Greeter.GreeterClient>(opts => opts.Address = new Uri("http://127.0.0.1"))
            .ConfigurePrimaryHttpMessageHandler(_ => new SocketsHttpHandler());

        await using var sp = serviceCollection.BuildServiceProvider();

        var factory = sp.GetRequiredService<GrpcClientFactory>();

        const string name = nameof(Greeter.GreeterClient);
        const int clientCount = 100;

        var barrier = new Barrier(clientCount);
        var tasks = new List<Task<Greeter.GreeterClient>>();

        for (int i = 0; i < clientCount; i++)
        {
            var t = Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    return factory.CreateClient<Greeter.GreeterClient>(name);
                },
                TaskCreationOptions.LongRunning);
            tasks.Add(t);
        }

        var clients = (await Task.WhenAll(tasks)).ToList();

        // All CallInvokers are the same, btw.
        var channels = clients.Select(cl => GetChannel(GetBaseCallInvoker(cl))).ToList();
        var ch0 = channels[0];
        Assert.That(channels, Is.All.SameAs(ch0));

        // Since call invokers are all the same, we need to check only one.
        int handlerType = GetHandlerType(ch0);
        Assert.That(handlerType, Is.Zero, "0 - SocketsHttpHandler, 3 - Custom");
    }

    private static CallInvoker GetBaseCallInvoker(ClientBase client)
    {
        var callInvoker = _CallInvokerGetter.Invoke(client, null);
        Debug.Assert(callInvoker is CallInvoker);

        // Unwrap call invoker
        while (true)
        {
            if (callInvoker.GetType().FullName != "Grpc.Core.Interceptors.InterceptingCallInvoker")
            {
                return (CallInvoker)callInvoker;
            }

            var innerField = callInvoker.GetType().GetField("invoker", BindingFlags.NonPublic | BindingFlags.Instance);
            Debug.Assert(innerField is not null);
            callInvoker = innerField.GetValue(callInvoker);
            Debug.Assert(callInvoker is CallInvoker);
        }
    }

    private static GrpcChannel GetChannel(CallInvoker callInvoker)
    {
        Debug.Assert(callInvoker.GetType().Name == "HttpClientCallInvoker");

        var channelGetter = callInvoker
            .GetType()
            .GetProperty("Channel", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetMethod;
        Debug.Assert(channelGetter is not null);

        var ret = channelGetter.Invoke(callInvoker, null);
        Debug.Assert(ret is GrpcChannel);
        return (GrpcChannel)ret;
    }

    private static int GetHandlerType(GrpcChannel channel)
    {
        var type = _HandlerTypeGetter.Invoke(channel, null);
        Debug.Assert(type is not null);
        var typeValue = Convert.ToInt32(type, CultureInfo.InvariantCulture);
        return typeValue;
    }
}
