#region Copyright notice and license
// Copyright 2015 gRPC authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Reflection.V1;

namespace Grpc.Reflection;

/// <summary>
/// Implementation of server reflection service (v1).
/// </summary>
public class ReflectionV1ServiceImpl : Grpc.Reflection.V1.ServerReflection.ServerReflectionBase
{
    readonly ReflectionServiceCore core;

    /// <summary>
    /// Creates a new instance of <c>ReflectionV1ServiceImpl</c>.
    /// </summary>
    public ReflectionV1ServiceImpl(IEnumerable<string> services, SymbolRegistry symbolRegistry)
    {
        core = new ReflectionServiceCore(services, symbolRegistry);
    }

    /// <summary>
    /// Creates a new instance of <c>ReflectionV1ServiceImpl</c>.
    /// </summary>
    public ReflectionV1ServiceImpl(IEnumerable<ServiceDescriptor> serviceDescriptors)
    {
        core = new ReflectionServiceCore(serviceDescriptors);
    }

    /// <summary>
    /// Creates a new instance of <c>ReflectionV1ServiceImpl</c>.
    /// </summary>
    public ReflectionV1ServiceImpl(params ServiceDescriptor[] serviceDescriptors) : this((IEnumerable<ServiceDescriptor>) serviceDescriptors)
    {
    }

    /// <summary>
    /// Processes a stream of server reflection requests.
    /// </summary>
    public override async Task ServerReflectionInfo(IAsyncStreamReader<ServerReflectionRequest> requestStream, IServerStreamWriter<ServerReflectionResponse> responseStream, ServerCallContext context)
    {
        while (await requestStream.MoveNext().ConfigureAwait(false))
        {
            var response = ProcessRequest(requestStream.Current);
            await responseStream.WriteAsync(response).ConfigureAwait(false);
        }
    }

    ServerReflectionResponse ProcessRequest(ServerReflectionRequest request)
    {
        var requestCase = request.MessageRequestCase switch
        {
            ServerReflectionRequest.MessageRequestOneofCase.FileByFilename => ReflectionServiceCore.RequestCase.FileByFilename,
            ServerReflectionRequest.MessageRequestOneofCase.FileContainingSymbol => ReflectionServiceCore.RequestCase.FileContainingSymbol,
            ServerReflectionRequest.MessageRequestOneofCase.ListServices => ReflectionServiceCore.RequestCase.ListServices,
            _ => ReflectionServiceCore.RequestCase.Unsupported
        };

        var stringValue = request.MessageRequestCase switch
        {
            ServerReflectionRequest.MessageRequestOneofCase.FileByFilename => request.FileByFilename,
            ServerReflectionRequest.MessageRequestOneofCase.FileContainingSymbol => request.FileContainingSymbol,
            _ => null
        };

        var coreResponse = core.ProcessRequest(requestCase, stringValue);
        return ToResponse(coreResponse);
    }

    static ServerReflectionResponse ToResponse(ReflectionServiceCore.Response coreResponse)
    {
        switch (coreResponse.Case)
        {
            case ReflectionServiceCore.ResponseCase.FileDescriptorResponse:
                return new ServerReflectionResponse
                {
                    FileDescriptorResponse = new FileDescriptorResponse { FileDescriptorProto = { coreResponse.FileDescriptorProtos! } }
                };
            case ReflectionServiceCore.ResponseCase.ListServicesResponse:
                var serviceResponses = new ListServiceResponse();
                foreach (var serviceName in coreResponse.ServiceNames!)
                {
                    serviceResponses.Service.Add(new ServiceResponse { Name = serviceName });
                }
                return new ServerReflectionResponse
                {
                    ListServicesResponse = serviceResponses
                };
            case ReflectionServiceCore.ResponseCase.ErrorResponse:
            default:
                return new ServerReflectionResponse
                {
                    ErrorResponse = new ErrorResponse { ErrorCode = coreResponse.ErrorCode, ErrorMessage = coreResponse.ErrorMessage }
                };
        }
    }
}
