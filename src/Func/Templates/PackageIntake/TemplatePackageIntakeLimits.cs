// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PackageIntake;

/// <summary>
/// Supplies finite per-intake limits and a per-service cap on outstanding intakes and content leases.
/// </summary>
internal sealed class TemplatePackageIntakeLimits(
    int maxArchiveBytes,
    int maxEntries,
    long maxExpandedBytes,
    long maxEntryBytes,
    int maxManifestBytes,
    int maxPaths,
    int maxPathLength,
    long maxBufferBytes,
    int maxActiveLeases)
{
    public int MaxArchiveBytes { get; } = ArrayBytes(maxArchiveBytes, nameof(maxArchiveBytes));

    public int MaxEntries { get; } = Positive(maxEntries, nameof(maxEntries));

    public long MaxExpandedBytes { get; } = Positive(maxExpandedBytes, nameof(maxExpandedBytes));

    public long MaxEntryBytes { get; } = Positive(maxEntryBytes, nameof(maxEntryBytes));

    public int MaxManifestBytes { get; } = ArrayBytes(maxManifestBytes, nameof(maxManifestBytes));

    public int MaxPaths { get; } = Positive(maxPaths, nameof(maxPaths));

    public int MaxPathLength { get; } = Positive(maxPathLength, nameof(maxPathLength));

    public long MaxBufferBytes { get; } = Positive(maxBufferBytes, nameof(maxBufferBytes));

    public int MaxActiveLeases { get; } = Positive(maxActiveLeases, nameof(maxActiveLeases));

    private static int ArrayBytes(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Array.MaxLength, parameterName);
        return value;
    }

    private static int Positive(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        return value;
    }

    private static long Positive(long value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        return value;
    }
}
