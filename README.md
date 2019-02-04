# gRPC for dotnet

## Experimental Only!

This repository is currently experimental and work-in-progress.
See https://github.com/grpc/grpc for the official version of gRPC C# (ready for production workloads).

## The Plan

We plan to implement a fully-managed version of gRPC for .NET that will be built on top of ASP.NET Core HTTP/2 server.
Here are some key features:
- API compatible with the existing gRPC C# implementation (your existing service implementations should work with minimal adjustments)
- Fully interoperable with other gRPC implementations (in other languages and other platforms)
- Good integration with the rest of ASP.NET Core ecosystem
- High-performance (we plan to utilize some of the cutting edge performance features from ASP.NET Core and in .NET plaform itself)
- We plan to provide a managed .NET Core client as well (possibly with limited feature set at first)

We are committed to delivering the managed server experience Microsoft.AspNetCore.Server functionalities in ASP.NET Core 3.0 timeframe. We will strive to also deliver the mananged client experience in 3.0.

See [doc/packages.md](doc/packages.md) for the planned package layout for both gRPC C# native (the current official version) and the new fully-managed gRPC for ASP.NET Core.

Please note that we plan both implementations (gRPC C# native and fully-managed gRPC for .NET Core) to coexist, there are currently no plans for one implementation to replace the other one.

## To start using gRPC for ASP.NET Core

Instructions coming soon!

## To develop gRPC for ASP.NET Core

Install [.NET Core SDK 3 preview 2](https://dotnet.microsoft.com/download/dotnet-core/3.0)
or [VS 2019 preview](https://visualstudio.microsoft.com/vs/preview/).

TODO(jtattermusch): add instructions for grabbing pre-release Grpc.Core.Api package.

```
dotnet build Grpc.AspNetCore.sln
```

To run tests:
```
dotnet tests Grpc.AspNetCore.sln
```

## To contribute

Contributions are welcome!

General rules for [contributing to the gRPC project](https://github.com/grpc/grpc/blob/master/CONTRIBUTING.md) apply for this repository.
