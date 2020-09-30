# grpc-dotnet benchmarks

grpc-dotnet benchmarks using the latest source code are run daily on the https://github.com/aspnet/benchmarks environment.

View the latest results [here](https://msit.powerbi.com/view?r=eyJrIjoiYTZjMTk3YjEtMzQ3Yi00NTI5LTg5ZDItNmUyMGRlOTkwMGRlIiwidCI6IjcyZjk4OGJmLTg2ZjEtNDFhZi05MWFiLTJkN2NkMDExZGI0NyIsImMiOjV9&pageName=ReportSection9567390a89a2d30b0eda).

## Run benchmarks locally

Benchmarks can be run locally from the command line.

Example of running client using Grpc.Net.Client against Grpc.AspNetCore server:

1. **Launch server:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcAspNetCoreServer\ --protocol h2c
2. **Launch client:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcClient\ -- -u http://localhost:5000 -c 10 --streams 50 -s unary -p h2c --grpcClientType grpcnetclient