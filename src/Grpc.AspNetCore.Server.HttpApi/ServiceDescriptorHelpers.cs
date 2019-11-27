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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Primitives;

namespace Grpc.AspNetCore.Server.HttpApi
{
    internal class ServiceDescriptorHelpers
    {
        public static ServiceDescriptor? GetServiceDescriptor(Type serviceReflectionType)
        {
            var property = serviceReflectionType.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                return (ServiceDescriptor?)property.GetValue(null);
            }

            throw new InvalidOperationException($"Get not find Descriptor property on {serviceReflectionType.Name}.");
        }

        public static bool TryResolveDescriptors(MessageDescriptor messageDescriptor, string variable, [NotNullWhen(true)]out List<FieldDescriptor>? fieldDescriptors)
        {
            fieldDescriptors = null;
            var path = variable.AsSpan();
            MessageDescriptor? currentDescriptor = messageDescriptor;

            while (path.Length > 0)
            {
                var separator = path.IndexOf('.');

                string fieldName;
                if (separator != -1)
                {
                    fieldName = path.Slice(0, separator).ToString();
                    path = path.Slice(separator + 1);
                }
                else
                {
                    fieldName = path.ToString();
                    path = ReadOnlySpan<char>.Empty;
                }

                var field = currentDescriptor?.FindFieldByName(fieldName);
                if (field == null)
                {
                    fieldDescriptors = null;
                    return false;
                }

                if (fieldDescriptors == null)
                {
                    fieldDescriptors = new List<FieldDescriptor>();
                }

                fieldDescriptors.Add(field);
                if (field.FieldType == FieldType.Message)
                {
                    currentDescriptor = field.MessageType;
                }
                else
                {
                    currentDescriptor = null;
                }

            }

            return true;
        }

        private static object ConvertValue(object value, FieldDescriptor descriptor)
        {
            switch (descriptor.FieldType)
            {
                case FieldType.Double:
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case FieldType.Float:
                    return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case FieldType.Int64:
                case FieldType.SInt64:
                case FieldType.SFixed64:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case FieldType.UInt64:
                case FieldType.Fixed64:
                    return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                case FieldType.Int32:
                case FieldType.SInt32:
                case FieldType.SFixed32:
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case FieldType.Bool:
                    return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                case FieldType.String:
                    return value;
                case FieldType.Bytes:
                    {
                        if (value is string s)
                        {
                            return ByteString.FromBase64(s);
                        }
                        throw new InvalidOperationException("Base64 encoded string required to convert to bytes.");
                    }
                case FieldType.UInt32:
                case FieldType.Fixed32:
                    return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case FieldType.Enum:
                    {
                        if (value is string s)
                        {
                            var enumValueDescriptor = descriptor.EnumType.FindValueByName(s);
                            if (enumValueDescriptor == null)
                            {
                                throw new InvalidOperationException($"Invalid enum value '{s}' for enum type {descriptor.EnumType.Name}.");
                            }

                            return enumValueDescriptor.Number;
                        }
                        throw new InvalidOperationException("String required to convert to enum.");
                    }
                case FieldType.Message:
                    if (IsWrapperType(descriptor.MessageType))
                    {
                        return ConvertValue(value, descriptor.MessageType.FindFieldByName("value"));
                    }
                    break;
            }

            throw new InvalidOperationException("Unsupported type: " + descriptor.FieldType);
        }

        public static void RecursiveSetValue(IMessage currentValue, List<FieldDescriptor> pathDescriptors, object values)
        {
            for (var i = 0; i < pathDescriptors.Count; i++)
            {
                var isLast = i == pathDescriptors.Count - 1;
                var field = pathDescriptors[i];

                if (isLast)
                {
                    if (field.IsRepeated)
                    {
                        var list = (IList)field.Accessor.GetValue(currentValue);
                        if (values is StringValues stringValues)
                        {
                            foreach (var value in stringValues)
                            {
                                list.Add(ConvertValue(value, field));
                            }
                        }
                        else
                        {
                            list.Add(ConvertValue(values, field));
                        }
                    }
                    else
                    {
                        if (values is StringValues stringValues)
                        {
                            if (stringValues.Count == 1)
                            {
                                field.Accessor.SetValue(currentValue, ConvertValue(stringValues[0], field));
                            }
                            else
                            {
                                throw new InvalidOperationException("Can't set multiple values onto a non-repeating field.");
                            }
                        }
                        else
                        {
                            field.Accessor.SetValue(currentValue, ConvertValue(values, field));
                        }
                    }
                }
                else
                {

                    var fieldMessage = (IMessage)field.Accessor.GetValue(currentValue);

                    if (fieldMessage == null)
                    {
                        fieldMessage = (IMessage)Activator.CreateInstance(field.MessageType.ClrType)!;
                        field.Accessor.SetValue(currentValue, fieldMessage);
                    }

                    currentValue = fieldMessage;
                }
            }
        }

        internal static bool IsWrapperType(MessageDescriptor m) => m.File.Package == "google.protobuf" && m.File.Name == "google/protobuf/wrappers.proto";
    }
}
