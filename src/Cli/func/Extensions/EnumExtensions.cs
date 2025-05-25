// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Extensions
{
    internal static class EnumExtensions
    {
        public static string GetDisplayString(this Enum enumVal)
        {
            System.Reflection.FieldInfo field = enumVal.GetType().GetField(enumVal.ToString());
            var attr = Attribute.GetCustomAttribute(field, typeof(DisplayStringAttribute)) as DisplayStringAttribute;
            return attr?.Value ?? enumVal.ToString();
        }
    }
}
