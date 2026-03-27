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

using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Grpc.Reflection;

internal sealed class ReflectionServiceCore
{
    readonly List<string> services;
    readonly SymbolRegistry symbolRegistry;

    public ReflectionServiceCore(IEnumerable<string> services, SymbolRegistry symbolRegistry)
    {
        this.services = new List<string>(services);
        this.symbolRegistry = symbolRegistry;
    }

    public ReflectionServiceCore(IEnumerable<ServiceDescriptor> serviceDescriptors)
    {
        this.services = new List<string>(serviceDescriptors.Select((serviceDescriptor) => serviceDescriptor.FullName));
        this.symbolRegistry = SymbolRegistry.FromFiles(serviceDescriptors.Select((serviceDescriptor) => serviceDescriptor.File));
    }

    public enum RequestCase
    {
        FileByFilename,
        FileContainingSymbol,
        ListServices,
        Unsupported
    }

    public enum ResponseCase
    {
        FileDescriptorResponse,
        ListServicesResponse,
        ErrorResponse
    }

    public readonly struct Response
    {
        public ResponseCase Case { get; }
        public IEnumerable<ByteString>? FileDescriptorProtos { get; }
        public IEnumerable<string>? ServiceNames { get; }
        public int ErrorCode { get; }
        public string? ErrorMessage { get; }

        private Response(ResponseCase responseCase, IEnumerable<ByteString>? fileDescriptorProtos = null, IEnumerable<string>? serviceNames = null, int errorCode = 0, string? errorMessage = null)
        {
            Case = responseCase;
            FileDescriptorProtos = fileDescriptorProtos;
            ServiceNames = serviceNames;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static Response FileDescriptor(IEnumerable<ByteString> fileDescriptorProtos)
            => new Response(ResponseCase.FileDescriptorResponse, fileDescriptorProtos: fileDescriptorProtos);

        public static Response Services(IEnumerable<string> serviceNames)
            => new Response(ResponseCase.ListServicesResponse, serviceNames: serviceNames);

        public static Response Error(int errorCode, string errorMessage)
            => new Response(ResponseCase.ErrorResponse, errorCode: errorCode, errorMessage: errorMessage);
    }

    public Response ProcessRequest(RequestCase requestCase, string? stringValue)
    {
        switch (requestCase)
        {
            case RequestCase.FileByFilename:
                return FileByFilename(stringValue!);
            case RequestCase.FileContainingSymbol:
                return FileContainingSymbol(stringValue!);
            case RequestCase.ListServices:
                return ListServices();
            default:
                return CreateErrorResponse(StatusCode.Unimplemented, "Request type not supported by C# reflection service.");
        }
    }

    Response FileByFilename(string filename)
    {
        FileDescriptor? file = symbolRegistry.FileByName(filename);
        if (file is null)
        {
            return CreateErrorResponse(StatusCode.NotFound, "File not found.");
        }

        return CreateFileDescriptorResponse(file);
    }

    Response FileContainingSymbol(string symbol)
    {
        FileDescriptor? file = symbolRegistry.FileContainingSymbol(symbol);
        if (file is null)
        {
            return CreateErrorResponse(StatusCode.NotFound, "Symbol not found.");
        }

        return CreateFileDescriptorResponse(file);
    }

    Response CreateFileDescriptorResponse(FileDescriptor file)
    {
        var transitiveDependencies = new HashSet<FileDescriptor>();
        CollectTransitiveDependencies(file, transitiveDependencies);

        return Response.FileDescriptor(transitiveDependencies.Select((d) => d.SerializedData));
    }

    Response ListServices()
    {
        return Response.Services(services);
    }

    static Response CreateErrorResponse(StatusCode status, string message)
    {
        return Response.Error((int)status, message);
    }

    static void CollectTransitiveDependencies(FileDescriptor descriptor, HashSet<FileDescriptor> pool)
    {
        pool.Add(descriptor);
        foreach (var dependency in descriptor.Dependencies)
        {
            if (pool.Add(dependency))
            {
                // descriptors cannot have circular dependencies
                CollectTransitiveDependencies(dependency, pool);
            }
        }
    }
}
