#region Copyright notice and license

// Copyright 2019 The gRPC Authors
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

using System.Linq;
using System.Threading.Tasks;
using Certify;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GRPCServer
{
    public class CertifierService : Certifier.CertifierBase
    {
        public override Task<CertificateInfoResponse> GetCertificateInfo(Empty request, ServerCallContext context)
        {
            // ClientCertificateMode in Kestrel must be configured to allow client certificates
            // https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.server.kestrel.https.httpsconnectionadapteroptions.clientcertificatemode

            // Use the following code to get the client certificate as an X509Certificate2 instance:
            //
            // var httpContext = context.GetHttpContext();
            // var clientCertificate = httpContext.Connection.ClientCertificate;

            var name = string.Join(',', context.AuthContext.PeerIdentity.Select(i => i.Name));
            var certificateInfo = new CertificateInfoResponse
            {
                HasCertificate = context.AuthContext.IsPeerAuthenticated,
                Name = name
            };

            return Task.FromResult(certificateInfo);
        }
    }
}
