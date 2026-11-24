// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Formatting shared by setup configuration and dependency planning.
/// </summary>
internal static class SetupRuntimes
{
    public static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? RangeText(NuGet.Versioning.VersionRange? range)
        => range is null ? null : range.OriginalString ?? range.ToString();
}
