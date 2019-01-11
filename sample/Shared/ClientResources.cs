using System.IO;
using Grpc.Core;

namespace Common
{
    public static class ClientResources
    {
        public static SslCredentials SslCredentials
            = new SslCredentials(
                File.ReadAllText(Path.Combine(Resources.CertDir, "ca.crt")),
                new KeyCertificatePair(
                    File.ReadAllText(Path.Combine(Resources.CertDir, "client.crt")),
                    File.ReadAllText(Path.Combine(Resources.CertDir, "client.key"))));
    }
}