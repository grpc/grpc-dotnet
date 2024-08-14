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
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class GrpcProtocolHelpersTests
{
    [Test]
    public void CreateAuthContext_CertWithAlternativeNames_UseAlternativeNamesAsPeerIdentity()
    {
        // Arrange
        var certificate = LoadCertificate(TestHelpers.ResolvePath(@"Certs/outlookcom.crt"));

        // Act
        var authContext = GrpcProtocolHelpers.CreateAuthContext(certificate);

        // Assert
        Assert.AreEqual(true, authContext.IsPeerAuthenticated);
        Assert.AreEqual(GrpcProtocolConstants.X509SubjectAlternativeNameKey, authContext.PeerIdentityPropertyName);

        var identity = authContext.PeerIdentity.ToList();

        Assert.AreEqual(23, identity.Count);
        Assert.AreEqual(GrpcProtocolConstants.X509SubjectAlternativeNameKey, identity[0].Name);
        Assert.AreEqual("*.internal.outlook.com", identity[0].Value);

        var allProperties = authContext.Properties.ToList();
        Assert.AreEqual(24, allProperties.Count);

        var commonName = authContext.FindPropertiesByName(GrpcProtocolConstants.X509CommonNameKey).Single();
        Assert.AreEqual(GrpcProtocolConstants.X509CommonNameKey, commonName.Name);
        Assert.AreEqual("outlook.com", commonName.Value);
    }

    [Test]
    public void CreateAuthContext_CertWithCommonName_UseCommonNameAsPeerIdentity()
    {
        // Arrange
        var certificate = LoadCertificate(TestHelpers.ResolvePath(@"Certs/client.crt"));

        // Act
        var authContext = GrpcProtocolHelpers.CreateAuthContext(certificate);

        // Assert
        Assert.AreEqual(true, authContext.IsPeerAuthenticated);
        Assert.AreEqual(GrpcProtocolConstants.X509CommonNameKey, authContext.PeerIdentityPropertyName);

        var identity = authContext.PeerIdentity.ToList();

        Assert.AreEqual(1, identity.Count);
        Assert.AreEqual(GrpcProtocolConstants.X509CommonNameKey, identity[0].Name);
        Assert.AreEqual("localhost", identity[0].Value);

        var allProperties = authContext.Properties.ToList();
        Assert.AreEqual(1, allProperties.Count);
    }

    [TestCase("1H", 36000000000, true)]
    [TestCase("1M", 600000000, true)]
    [TestCase("1S", 10000000, true)]
    [TestCase("1m", 10000, true)]
    [TestCase("1u", 10, true)]
    [TestCase("100n", 1, true)]
    [TestCase("1n", 0, true)]
    [TestCase("0S", 0, true)]
    [TestCase("", 0, false)]
    [TestCase("5", 0, false)]
    [TestCase("M", 0, false)]
    public void TryDecodeTimeout_WithVariousUnits_ShouldMatchExpected(string timeout, long expectedTicks, bool expectedSuccesfullyDecoded)
    {
        // Arrange
        var expectedTimespan = new TimeSpan(expectedTicks);

        // Act
        var successfullyDecoded = GrpcProtocolHelpers.TryDecodeTimeout(timeout, out var timeSpan);

        // Assert
        Assert.AreEqual(expectedSuccesfullyDecoded, successfullyDecoded);
        Assert.AreEqual(expectedTimespan, timeSpan);
    }

    public static X509Certificate2 LoadCertificate(string path)
    {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadCertificateFromFile(path);
#else
        return new X509Certificate2(path);
#endif
    }
}
