# grpc-dotnet benchmarks

grpc-dotnet benchmarks using the latest source code are run daily on the https://github.com/aspnet/benchmarks environment.

View the latest results [here](https://msit.powerbi.com/view?r=eyJrIjoiYTZjMTk3YjEtMzQ3Yi00NTI5LTg5ZDItNmUyMGRlOTkwMGRlIiwidCI6IjcyZjk4OGJmLTg2ZjEtNDFhZi05MWFiLTJkN2NkMDExZGI0NyIsImMiOjV9&pageName=ReportSection9567390a89a2d30b0eda).

## Run benchmarks locally

Benchmarks can be run locally from the command line.

Example of running client using Grpc.Net.Client against Grpc.AspNetCore server:

1. **Launch server:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcAspNetCoreServer\ --protocol h2c
2. **Launch client:** dotnet run -c Release -p .\perf\benchmarkapps\GrpcClient\ -- -u http://localhost:5000 -c 10 --streams 50 -s unary -p h2c --grpcClientType grpcnetclient

## QpsWorker

The `QpsWorker` runs in the [gRPC benchmark environment](https://grpc.io/docs/guides/benchmarking/). The worker hosts gRPC services which are used to start a benchmark server or client.

`grpcui` can be used to test the worker. Specify `--LogLevel Debug` argument to enable server and client console logging.

### Start server

`RunServer` method with request:

```json
[
  {
    "setup": {
      "serverType": "ASYNC_SERVER",
      "port": 5002,
      "coreList": [],
      "channelArgs": [],
      "securityParams": {}
    }
  }
]
```

Or for generic server:

```json
[
  {
    "setup": {
      "serverType": "ASYNC_GENERIC_SERVER",
      "securityParams": {},
      "port": 5002,
      "coreList": [],
      "channelArgs": [],
      "payloadConfig": {
        "bytebufParams": {
          "reqSize": 50,
          "respSize": 50
        }
      }
    }
  }
]
```

### Start client

`RunClient` method with request:

```json
[
  {
    "setup": {
      "serverTargets": [
        "localhost:5002"
      ],
      "coreList": [],
      "channelArgs": [],
      "clientType": "ASYNC_CLIENT",
      "securityParams": {},
      "clientChannels": 20,
      "rpcType": "UNARY",
      "outstandingRpcsPerChannel": 50,
      "histogramParams": {
        "resolution": 50,
        "maxPossible": 50
      },
      "loadParams": {
        "closedLoop": {}
      },
      "payloadConfig": {
        "simpleParams": {
          "reqSize": 50,
          "respSize": 50
        }
      }
    }
  }
]
```

Or for generic server:

```json
[
  {
    "setup": {
      "serverTargets": [
        "localhost:5002"
      ],
      "clientType": "ASYNC_CLIENT",
      "securityParams": {},
      "outstandingRpcsPerChannel": 50,
      "clientChannels": 20,
      "rpcType": "STREAMING",
      "loadParams": {
        "closedLoop": {}
      },
      "payloadConfig": {
        "bytebufParams": {
          "reqSize": 50,
          "respSize": 50
        }
      },
      "histogramParams": {
        "resolution": 50,
        "maxPossible": 50
      },
      "coreList": [],
      "channelArgs": []
    }
  }
]
```