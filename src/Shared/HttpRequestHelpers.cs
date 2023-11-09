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

using System.Diagnostics.CodeAnalysis;

namespace Grpc.Shared;

internal static class HttpRequestHelpers
{
    public static bool TryGetOption<T>(this HttpRequestMessage requestMessage, string key, [NotNullWhen(true)] out T? value)
    {
#if NET5_0_OR_GREATER
        return requestMessage.Options.TryGetValue(new HttpRequestOptionsKey<T>(key), out value!);
#else
        if (requestMessage.Properties.TryGetValue(key, out var tempValue))
        {
            value = (T)tempValue!;
            return true;
        }
        value = default;
        return false;
#endif
    }

    public static void SetOption<T>(this HttpRequestMessage requestMessage, string key, T value)
    {
#if NET5_0_OR_GREATER
        requestMessage.Options.Set(new HttpRequestOptionsKey<T>(key), value);
#else
        requestMessage.Properties[key] = value;
#endif
    }

    public static bool HasHttpHandlerType(HttpMessageHandler handler, string handlerTypeName)
    {
        return GetHttpHandlerType(handler, handlerTypeName) != null;
    }

    public static HttpMessageHandler? GetHttpHandlerType(HttpMessageHandler handler, string handlerTypeName)
    {
        if (IsType(handler.GetType(), handlerTypeName))
        {
            return handler;
        }

        HttpMessageHandler? currentHandler = handler;
        while (currentHandler is DelegatingHandler delegatingHandler)
        {
            currentHandler = delegatingHandler.InnerHandler;
            if (currentHandler != null && IsType(currentHandler.GetType(), handlerTypeName))
            {
                return currentHandler;
            }
        }

        return null;
    }

    private static bool IsType(Type type, string handlerTypeName)
    {
        Type? currentType = type;
        do
        {
            if (currentType.FullName == handlerTypeName)
            {
                return true;
            }

        } while ((currentType = currentType.BaseType) != null);

        return false;
    }

    public static bool HasHttpHandlerType<T>(HttpMessageHandler handler) where T : HttpMessageHandler
    {
        return GetHttpHandlerType<T>(handler) != null;
    }

    public static T? GetHttpHandlerType<T>(HttpMessageHandler handler) where T : HttpMessageHandler
    {
        if (handler is T t)
        {
            return t;
        }

        HttpMessageHandler? currentHandler = handler;
        while (currentHandler is DelegatingHandler delegatingHandler)
        {
            currentHandler = delegatingHandler.InnerHandler;

            if (currentHandler is T inner)
            {
                return inner;
            }
        }

        return null;
    }
}
