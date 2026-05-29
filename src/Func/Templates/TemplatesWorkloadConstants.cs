// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Templates-workload identity helpers (package id prefix + per-stack id
/// construction). Kept off the public <see cref="IInstalledTemplatesWorkloads"/>
/// surface because these helpers are CLI-internal.
/// </summary>
internal static class TemplatesWorkloadConstants
{
    /// <summary>
    /// Builds the canonical NuGet package id for a templates content workload
    /// targeting <paramref name="stack"/>. Always uses the cased prefix; the
    /// workload registry stores ids lowercased per NuGet normalisation so
    /// callers comparing against registry rows must use case-insensitive
    /// matching.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="stack"/> is null, empty, or whitespace.
    /// </exception>
    public static string GetPackageId(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            throw new ArgumentException("Stack must be non-empty.", nameof(stack));
        }

        // Title-case the first letter so the resulting id reads as
        // "Azure.Functions.Cli.Workloads.Templates.Node". Registry comparisons
        // are case-insensitive so the cased form is purely cosmetic for
        // hints / log messages.
        string trimmed = stack.Trim();
        string cased = char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
        return $"{IInstalledTemplatesWorkloads.TemplatesWorkloadPackageIdPrefix}.{cased}";
    }
}
