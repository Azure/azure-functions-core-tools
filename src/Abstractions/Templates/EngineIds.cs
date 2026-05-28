// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Stable engine identifiers used by <see cref="ITemplateEngineProvider.EngineId"/>
/// and <see cref="FunctionTemplateInfo.EngineId"/>. CLI-internal dispatch keys —
/// never surfaced in user-facing output, flags, schema fields, or help text.
/// </summary>
public static class EngineIds
{
    /// <summary>
    /// V2 engine — Node and Python templates (`NewTemplate[]` jobs/actions DSL,
    /// `$(KEY)` substitution). Engine zone: workload payload's <c>content/v2/</c>.
    /// </summary>
    public const string V2 = "v2";

    /// <summary>
    /// DotNet engine — isolated-worker .NET templates. Catalog and
    /// per-template <c>--help</c> source from <c>dotnet-templates.json</c>;
    /// scaffold via <c>dotnet new &lt;shortName&gt;</c> shell-out against the
    /// workload-install-time provisioned hive. Engine zone: workload
    /// payload's <c>content/dotnet-templates.json</c>.
    /// </summary>
    public const string DotNet = "dotnet";
}
