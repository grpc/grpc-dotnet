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

using Grpc.AspNetCore.Server.Internal;
using Grpc.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grpc.AspNetCore.Server.Model.Internal;

internal sealed class GrpcUnimplementedConstraint : IRouteConstraint
{
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        if (httpContext == null)
        {
            return false;
        }

        // Constraint needs to be valid when a CORS preflight request is received so that CORS middleware will run
        if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
        {
            return true;
        }

        if (!HttpMethods.IsPost(httpContext.Request.Method))
        {
            return false;
        }

        return CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, httpContext.Request.ContentType) ||
            CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebContentType, httpContext.Request.ContentType) ||
            CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebTextContentType, httpContext.Request.ContentType);
    }

    public GrpcUnimplementedConstraint()
    {
    }
}
