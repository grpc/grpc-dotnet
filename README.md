# gRPC for .NET

## Preview out!

A preview of gRPC for ASP.NET Core is on [NuGet](https://www.nuget.org/packages/Grpc.AspNetCore.Server). A template using the preview [shipped with .NET Core 3.0 Preview 3](https://devblogs.microsoft.com/aspnet/asp-net-core-updates-in-net-core-3-0-preview-3/).

See https://github.com/grpc/grpc for the official version of gRPC C# (ready for production workloads).

## The Plan

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

Documentation and guides are coming soon! In the mean time we suggest creating a basic website using the gRPC for ASP.NET template that comes with .NET Core 3.0 and refer to the examples at https://github.com/grpc/grpc-dotnet/tree/master/examples/Server.

## To develop gRPC for ASP.NET Core

Setting up local feed with unreleased Grpc.* packages:
```
# We may depend on unreleased Grpc.* packages.
# Run this script before building the project.
./build/get-grpc.sh or ./build/get-grpc.ps1
```

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
