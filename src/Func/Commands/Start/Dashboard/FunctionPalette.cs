// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Hash-based color assignment for function names. Uses a broad palette with
/// red excluded because red is reserved for errors. Stable across runs and
/// across renderers: the same name always maps to the same color.
/// </summary>
internal sealed class FunctionPalette
{
    /// <summary>
    /// Spectre.Console color names. Picked to avoid very light and very dark
    /// extremes so labels remain legible on both light and dark terminal themes.
    /// </summary>
    private static readonly string[] _palette =
    [
        "royalblue1",
        "springgreen4",
        "mediumpurple3_1",
        "magenta3_1",
        "green4",
        "darkorange3",
        "teal",
        "turquoise4",
        "magenta3_2",
        "mediumpurple3",
        "purple_2",
        "chartreuse4",
        "olive",
        "deepskyblue3",
        "mediumorchid3",
        "slateblue1",
        "darkseagreen4",
        "slateblue3_1",
        "green",
        "dodgerblue2",
        "plum4",
        "paleturquoise4",
        "magenta2",
        "deepskyblue3_1",
        "mediumorchid",
        "yellow4",
        "darkorange3_1",
        "darkviolet_1",
        "steelblue",
        "slateblue3",
        "wheat4",
        "orange4_1",
        "dodgerblue3",
        "mediumpurple2",
        "dodgerblue1",
        "steelblue3",
        "mediumpurple4",
        "purple_1",
        "magenta3",
        "magenta2_1",
        "darkgoldenrod",
        "cornflowerblue",
        "deepskyblue4_2",
    ];

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the palette entry for <paramref name="functionName"/>. Caches
    /// the result so repeated lookups don't rehash.
    /// </summary>
    public string GetColorFor(string functionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        return _cache.GetOrAdd(functionName, static name => _palette[(int)(Fnv1a(name) % (uint)_palette.Length)]);
    }

    private static uint Fnv1a(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
