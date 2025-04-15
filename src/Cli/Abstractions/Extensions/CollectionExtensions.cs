// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Extensions/CollectionsExtensions.cs

namespace Azure.Functions.Cli.Abstractions.Extensions
{
    public static class CollectionsExtensions
    {
        public static IEnumerable<T> OrEmptyIfNull<T>(this IEnumerable<T> enumerable)
        {
            return enumerable == null
                ? Enumerable.Empty<T>()
                : enumerable;
        }
    }
}
