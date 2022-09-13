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

using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrelOptions =>
{
    kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
    });
});

builder.Services.AddGrpc();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
    .AddCertificate(options =>
    {
        // Not recommended in production environments. The example is using a self-signed test certificate.
        options.RevocationMode = X509RevocationMode.NoCheck;
        options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
        options.AllowedCertificateTypes = CertificateTypes.All;
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<CertifierService>();

app.Run();
