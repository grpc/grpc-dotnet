# gRPC for .NET

## Preview out!

A preview of gRPC for ASP.NET Core is on [NuGet](https://www.nuget.org/packages/Grpc.AspNetCore.Server). A template using the preview [shipped with .NET Core 3.0 Preview 3](https://devblogs.microsoft.com/aspnet/asp-net-core-updates-in-net-core-3-0-preview-3/).

See https://github.com/grpc/grpc for the official version of gRPC C# (ready for production workloads).

## The plan

We plan to implement a fully-managed version of gRPC for .NET that will be built on top of ASP.NET Core HTTP/2 server.
Here are some key features:
- API compatible with the existing gRPC C# implementation (your existing service implementations should work with minimal adjustments)
- Fully interoperable with other gRPC implementations (in other languages and other platforms)
- Good integration with the rest of the ASP.NET Core ecosystem
- High-performance (we plan to utilize some of the cutting edge performance features from ASP.NET Core and in the .NET platform itself)
- We plan to provide a managed .NET Core client as well (possibly with limited feature set at first)

We are committed to delivering the managed server experience Microsoft.AspNetCore.Server functionalities in the ASP.NET Core 3.0 timeframe. We will strive to also deliver the mananged client experience in 3.0.

See [doc/packages.md](doc/packages.md) for the planned package layout for both gRPC C# native (the current official version) and the new fully-managed gRPC for ASP.NET Core.

Please note that we plan for both implementations (gRPC C# native and fully-managed gRPC for .NET Core) to coexist; there are currently no plans for one implementation to replace the other one.

## To start using gRPC for ASP.NET Core

The best place to start using gRPC for ASP.NET Core is the gRPC template that comes with .NET Core 3.0. Use the template to [create a gRPC service website](https://docs.microsoft.com/en-us/aspnet/core/tutorials/grpc/grpc-start).

Additional documentation and tutorials are available on docs.microsoft.com. Read more at [An introduction to gRPC on ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/grpc/).

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
dotnet build Grpc.AspNetCore.sln
```

To run tests from the command line:
```
dotnet test Grpc.AspNetCore.sln
```

## To contribute

Contributions are welcome!

General rules for [contributing to the gRPC project](https://github.com/grpc/grpc/blob/master/CONTRIBUTING.md) apply for this repository.
