# Microsoft support for grpc-dotnet

***gRPC is a CNCF project and receives support from the gRPC team and the grpc-dotnet maintainers and contributors. All gRPC languages officially supported by CNCF have this level of support.***

***In addition, grpc-dotnet is an important technology for the .NET ecosystem. It also receives official support directly from Microsoft's support team. This document summarized the details of Microsoft's support policy.***

Microsoft supports grpc-dotnet on multiple operating systems, per the [Microsoft Modern Lifecycle](https://support.microsoft.com/help/30881/modern-lifecycle-policy).

Support is provided for the following grpc-dotnet packages:

* Grpc.AspNetCore
* Grpc.AspNetCore.Server
* Grpc.AspNetCore.Web
* Grpc.AspNetCore.Healthchecks
* Grpc.AspNetCore.Server.ClientFactory
* Grpc.AspNetCore.Server.Reflection
* Grpc.Net.Client
* Grpc.Net.ClientFactory
* Grpc.Net.Client.Web
* Grpc.Net.Common
* dotnet-grpc

Notes:
* Applications must be using a [currently supported .NET release](https://dotnet.microsoft.com/platform/support/policy).
* Minimum supported grpc-dotnet version is currently v2.59.0. The minimum grpc-dotnet version supported is increased when major new .NET versions are released.
* Minimum supported version is the earliest major and minor release required to obtain assisted support. Please utilize public community channels for assistance or log issues directly on GitHub for releases before the minimum supported version.
* Assisted support is only available for the official builds released from https://github.com/grpc/grpc-dotnet, and no assisted support option is available for individual forks.
* Please note that new features and security\bug fixes are provided in the latest released version and are not backported to previous versions. To obtain the latest updates and features, please upgrade to the latest version.

Support has two key benefits:

* Patches are provided (for free) as required for functional or security issues, typically [every 6 weeks](doc/release_process.md).
* You can [contact Microsoft support to request help](https://support.serviceshub.microsoft.com/supportforbusiness/onboarding) (potentially at a cost).

You can also request community support on GitHub (for free).

Support is conditional on using the latest .NET and grpc-dotnet patch update and a supported operating system, as defined by:

* [Microsoft support policy](https://dotnet.microsoft.com/platform/support/policy)
* [.NET releases](releases.md)
* [.NET release policies](release-policies.md)
* [.NET supported operating system lifecycle](os-lifecycle-policy.md).

Knowing key dates for a product lifecycle helps you make informed decisions about when to upgrade or make other changes to your software and computing environment.
