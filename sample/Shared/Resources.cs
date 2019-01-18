using System;
using System.IO;

namespace Common
{
    public static class Resources
    {
        public static string CertDir = Path.Combine(GetSolutionDirectory(), "sample", "Certs");
        public static string ServerPFXPath = Path.Combine(CertDir, "server.pfx");

        private static string GetSolutionDirectory()
        {
            var applicationBasePath = AppContext.BaseDirectory;

            var directoryInfo = new DirectoryInfo(applicationBasePath);
            do
            {
                var solutionFileInfo = new FileInfo(Path.Combine(directoryInfo.FullName, "Grpc.AspNetCore.sln"));
                if (solutionFileInfo.Exists)
                {
                    return directoryInfo.FullName;
                }

                directoryInfo = directoryInfo.Parent;
            } while (directoryInfo.Parent != null);

            throw new InvalidOperationException($"Solution directory could not be found for {applicationBasePath}.");
        }
    }
}