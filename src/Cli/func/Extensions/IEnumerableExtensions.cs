// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Extensions
{
    internal static class IEnumerableExtensions
    {
        public static IEnumerable<T> NotDefaults<T>(this IEnumerable<T> collection)
        {
            return collection.Where(e => !EqualityComparer<T>.Default.Equals(e, default));
        }
    }
}
