# Microsoft support for grpc-dotnet

[Microsoft supports grpc-dotnet](https://LINK-TODO) on multiple operating systems, per the [Microsoft Modern Lifecycle](https://support.microsoft.com/help/30881/modern-lifecycle-policy).

Support is provided for the following grpc-dotnet packages:
* Grpc.AspNetCore
* Grpc.AspNetCore.Server
* Grpc.AspNetCore.Web
* Grpc.AspNetCore.Server.ClientFactory
* Grpc.AspNetCore.Server.Reflection
* Grpc.Net.Client
* Grpc.Net.ClientFactory
* Grpc.Net.Common
* dotnet-grpc

Notes:
* Currently supported grpc-dotnet versions are v2.37.0 or later.
* Applications must be using a [currently supported .NET release](https://dotnet.microsoft.com/platform/support/policy).
* Minimum supported version is the earliest major and minor release required to obtain assisted support. Please utilize public community channels for assistance or log issues directly on GitHub for releases prior to the minimum supported version.
* Assisted support is only available for the official builds released from https://github.com/grpc/grpc-dotnet, and no assisted support option is available for individual forks.
* Please note that new features and security\bug fixes are provided in the latest released version and are not backported to previous versions. To obtain the latest updates and features, please upgrade to the latest available version.

Support has two key benefits:

* Patches are provided (for free) as required for functional or security issues, typically [every 6 weeks](doc/release_process.md).
* You can [contact Microsoft support to request help](https://support.serviceshub.microsoft.com/supportforbusiness/onboarding) (potentially at a cost).

You can also request community support on GitHub (for free), but there is no guarantee on a quick reply.

Support is conditional on using the latest .NET and grpc-dotnet patch update and a supported operating system, as defined by:

* [Microsoft support policy](https://dotnet.microsoft.com/platform/support/policy)
* [.NET releases](releases.md)
* [.NET release policies](release-policies.md)
* [.NET supported operating system lifecycle](os-lifecycle-policy.md).

Knowing key dates for a product lifecycle helps you make informed decisions about when to upgrade or make other changes to your software and computing environment.
