// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Note that this file is copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Extensions/CollectionsExtensions.cs
// Once the dotnet cli utils package is in a published consumable state, we will migrate over to use that
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
