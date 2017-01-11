using System;
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
