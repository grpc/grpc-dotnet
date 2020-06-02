# grpc-dotnet benchmarks

grpc-dotnet benchmarks using the latest source code are run daily on the https://github.com/aspnet/benchmarks environment.

## Run benchmarks locally

Benchmarks can be run locally from the command line.

Example of running client using Grpc.Net.Client against Grpc.AspNetCore server:

1. **Launch server:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcAspNetCoreServer\ --protocol h2
2. **Launch client:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcClient\ -- -u http://localhost:5000 -c 100 -s unary -p h2 --grpcClientType grpcnetclient