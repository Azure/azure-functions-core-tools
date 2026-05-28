// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Default implementation that resolves the template hive path, honoring the
/// <c>FUNC_CLI_DOTNET_TEMPLATE_HIVE</c> environment variable when set, and
/// otherwise falling back to <c>&lt;func cli home&gt;/dotnet-template-hive</c>
/// (where the home itself honors <see cref="Constants.FuncHomeEnvironmentVariable"/>
/// and defaults to <c>~/.azure-functions</c>).
/// </summary>
internal sealed class TemplateHivePathProvider : ITemplateHivePathProvider
{
    internal const string HivePathEnvironmentVariable = "FUNC_CLI_DOTNET_TEMPLATE_HIVE";
    private const string HiveDirectoryName = "dotnet-template-hive";

    public string HivePath { get; }
    public string TimestampPath { get; }

    public TemplateHivePathProvider()
    {
        // Read directly from the process environment (not through IConfiguration) so the
        // template hive cannot be redirected by host config, global config, or project
        // .func/config.json. Only an explicit env var set by the user should control this.
        string? configured = Environment.GetEnvironmentVariable(HivePathEnvironmentVariable);

        string hivePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(FuncHomeResolver.Resolve(), HiveDirectoryName)
            : configured;

        HivePath = Path.GetFullPath(hivePath);
        TimestampPath = Path.Combine(HivePath, ".installed");
    }
}
