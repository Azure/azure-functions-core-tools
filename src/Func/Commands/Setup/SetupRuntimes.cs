// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Runtime naming shared by feature resolution and dependency planning.
/// </summary>
internal static class SetupRuntimes
{
    public const string DotNetFeature = "dotnet";
    public const string DotNetProfileRuntime = "dotnet-isolated";

    public static bool IsDotNetRuntime(string runtime)
        => string.Equals(runtime, DotNetFeature, StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtime, DotNetProfileRuntime, StringComparison.OrdinalIgnoreCase);

    public static SetupBundlePolicy GetBundlePolicy(string workerRuntime)
        => IsDotNetRuntime(workerRuntime)
            ? SetupBundlePolicy.NotSupported
            : SetupBundlePolicy.DefaultStable;

    public static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? RangeText(NuGet.Versioning.VersionRange? range)
        => range is null ? null : range.OriginalString ?? range.ToString();
}
