// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Extensions/CollectionsExtensions.cs
namespace Azure.Functions.Cli.Abstractions
{
    public static class CollectionExtensions
    {
        public static IEnumerable<T> OrEmptyIfNull<T>(this IEnumerable<T> enumerable)
        {
            return enumerable == null
                ? Enumerable.Empty<T>()
                : enumerable;
        }
    }
}
