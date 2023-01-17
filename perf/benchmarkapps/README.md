# grpc-dotnet benchmarks

grpc-dotnet benchmarks using the latest source code are run daily on the https://github.com/aspnet/benchmarks environment.

View the latest results [here](https://msit.powerbi.com/view?r=eyJrIjoiYTZjMTk3YjEtMzQ3Yi00NTI5LTg5ZDItNmUyMGRlOTkwMGRlIiwidCI6IjcyZjk4OGJmLTg2ZjEtNDFhZi05MWFiLTJkN2NkMDExZGI0NyIsImMiOjV9&pageName=ReportSection9567390a89a2d30b0eda).

## Run benchmarks locally

The benchmark environment runs the tests using `qps_json_driver`. The driver is a C++ app in https://github.com/grpc/grpc repo that is only buildable on Unix based operating systems.

Because the driver is challenging to get setup, the benchmarks can be run locally from the command line for quick testing. Note that there can be some differences in behavior.

Examples of running client using Grpc.Net.Client against Grpc.AspNetCore server

### TCP sockets

1. **Launch server:** dotnet run -c Release --project .\perf\benchmarkapps\GrpcAspNetCoreServer\ --protocol h2c
2. **Launch client:** dotnet run -c Release --project .\perf\benchmarkapps\GrpcClient\ -- -u http://localhost:5000 -c 10 --streams 50 -s unary -p h2c --grpcClientType grpcnetclient

### Named pipes

1. **Launch server:** dotnet run -c Release --project .\perf\benchmarkapps\GrpcAspNetCoreServer\ --protocol h2c --namedPipeName PerfPipe
2. **Launch client:** dotnet run -c Release --project .\perf\benchmarkapps\GrpcClient\ -- -u http://localhost:5000 -c 10 --streams 50 -s unary -p h2c --grpcClientType grpcnetclient --namedPipeName PerfPipe

## QpsWorker

The `QpsWorker` runs in the [gRPC benchmark environment](https://grpc.io/docs/guides/benchmarking/). The worker hosts gRPC services which are used to start a benchmark server or client.

* `driver_port` - Port for the worker. Consistent with other drivers.
* `LogLevel` - Logging level of the client and server runners. Optional. Defaults to no logging.

```cmd
dotnet run -c Release -- --LogLevel Warning --driver_port 5000
```

To test a running worker:

* `worker-start-server.ps1` starts a server.
* `worker-start-client.ps1` starts a client.
