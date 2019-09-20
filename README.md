# gRPC for .NET

## Available now on .NET Core 3.0!

[gRPC](https://grpc.io/) is a popular, high-performance RPC (remote procedure call) framework that offers an opinionated contract-first approach to API development. It uses modern technologies such as HTTP/2 for transport, and Protocol Buffers as the interface description language and binary serialization format. gRPC provides features such as authentication, bidirectional streaming and flow control, and cancellation and timeouts.

gRPC functionality for .NET Core 3.0 includes:

* [Grpc.AspNetCore](https://www.nuget.org/packages/Grpc.AspNetCore) &ndash; An ASP.NET Core framework for hosting gRPC services. gRPC on ASP.NET Core integrates with standard ASP.NET Core features like logging, dependency injection (DI), authentication and authorization.
* [Grpc.Net.Client](https://www.nuget.org/packages/Grpc.Net.Client) &ndash; A gRPC client for .NET Core that builds upon the familiar `HttpClient`.
* [Grpc.Net.ClientFactory](https://www.nuget.org/packages/Grpc.Net.ClientFactory) &ndash; gRPC client integration with `HttpClientFactory`.

Please note that gRPC for .NET does not replace [gRPC for C#](https://grpc.io/docs/quickstart/csharp/) (gRPC C# API over native C-core binaries). These implementations coexist; there are currently no plans for one implementation to replace the other one. gRPC for C# is the recommended solution for frameworks that gRPC for .NET does not support such as .NET Framework and Xamarin.

For more information, see [An introduction to gRPC on .NET](https://docs.microsoft.com/aspnet/core/grpc/).

## To start using gRPC for .NET

The best place to start using gRPC for ASP.NET Core is the gRPC template that comes with .NET Core 3.0. Use the template to [create a gRPC service website and client](https://docs.microsoft.com/aspnet/core/tutorials/grpc/grpc-start).

For additional examples of using gRPC in .NET refer to https://github.com/grpc/grpc-dotnet/tree/master/examples.

## gRPC NuGet feed

Official versions of gRPC are published to [NuGet.org](https://www.nuget.org/profiles/grpc-packages). This is the recommended place for most developers to get gRPC packages.

Nightly versions of gRPC for ASP.NET Core are published to the gRPC NuGet repository at https://grpc.jfrog.io/grpc/api/nuget/v3/grpc-nuget-dev. It is recommended to use a nightly gRPC package if you are using a nightly version of .NET Core, and vice-versa. There may be incompatibilities between .NET Core and gRPC for ASP.NET Core if a newer version of one is used with an older version of the other.

To use the gRPC NuGet repository and get the latest packages from it, place a `NuGet.config` file with the gRPC repository setup in your solution folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <!-- Add this repository to the list of available repositories -->
        <add key="gRPC repository" value="https://grpc.jfrog.io/grpc/api/nuget/v3/grpc-nuget-dev" />
    </packageSources>
</configuration>
```

Additional instructions for configuring a project to use a custom NuGet repository are available at [Changing NuGet configuration settings](https://docs.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior#changing-config-settings).

## To develop gRPC for ASP.NET Core

Installing .NET Core SDK:
```
# Run this script before building the project.
./build/get-dotnet.sh or ./build/get-dotnet.ps1
```

Set up the development environment to use the installed .NET Core SDK:
```
# Source this script to use the installed .NET Core SDK.
source ./activate.sh or . ./activate.ps1
```
To launch Visual Studio with the installed SDK:
```
# activate.sh or activate.ps1 must be sourced first, see previous step
startvs.cmd
```

To build from the command line:
```
dotnet build Grpc.DotNet.sln
```

To run tests from the command line:
```
dotnet test Grpc.DotNet.sln
```

## To contribute

Contributions are welcome!

General rules for [contributing to the gRPC project](https://github.com/grpc/grpc/blob/master/CONTRIBUTING.md) apply for this repository.
