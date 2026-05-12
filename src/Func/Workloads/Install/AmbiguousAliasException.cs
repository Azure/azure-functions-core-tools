// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Thrown when an alias provided to <c>func workload install</c> matches
/// more than one package id in the catalog (spec §6.1 step 2). The user
/// must re-run with <c>--exact</c> and a specific package id.
/// </summary>
internal sealed class AmbiguousAliasException : Exception
{
    public AmbiguousAliasException(string alias, IReadOnlyList<string> packageIds)
        : base(BuildMessage(alias, packageIds))
    {
        Alias = alias;
        PackageIds = packageIds;
    }

    public string Alias { get; }

    public IReadOnlyList<string> PackageIds { get; }

    private static string BuildMessage(string alias, IReadOnlyList<string> packageIds)
        => $"Alias '{alias}' matches multiple packages: {string.Join(", ", packageIds)}. "
            + "Re-run with --exact <packageId>.";
}
