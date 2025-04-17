// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;

namespace Azure.Functions.Cli.Extensions
{
    public static class TypeExtensions
    {
        public static bool IsGenericEnumerable(this Type type)
        {
            return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type);
        }
    }
}
