using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Grpc.AspNetCore.Server.Feature
{
    public interface IServerCallContextFeature
    {
        ServerCallContext ServerCallContext { get; }
    }
}
