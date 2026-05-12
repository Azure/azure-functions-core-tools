// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Hash-based color assignment for function names. Uses an 8-color palette
/// (red excluded — red is reserved for errors). Stable across runs and
/// across renderers: the same name always maps to the same color.
/// </summary>
internal sealed class FunctionPalette
{
    /// <summary>
    /// Spectre.Console color names. Picked to be distinct under both light
    /// and dark terminal themes; red is intentionally absent.
    /// </summary>
    private static readonly string[] _palette =
    [
        "blue",
        "cyan",
        "magenta1",
        "yellow",
        "green",
        "blue1",
        "magenta2",
        "cyan1",
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
