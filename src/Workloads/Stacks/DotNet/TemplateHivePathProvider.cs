// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Default implementation that resolves the template hive path, honoring the
/// <c>FUNC_CLI_DOTNET_TEMPLATE_HIVE</c> environment variable when set, and
/// falling back to <c>~/.azure-functions/dotnet-template-hive</c>.
/// </summary>
internal sealed class TemplateHivePathProvider : ITemplateHivePathProvider
{
    internal const string HivePathEnvironmentVariable = "FUNC_CLI_DOTNET_TEMPLATE_HIVE";
    private const string HiveDirectoryName = "dotnet-template-hive";

    public string HivePath { get; }
    public string TimestampPath { get; }

    public TemplateHivePathProvider()
    {
        string? configured = Environment.GetEnvironmentVariable(HivePathEnvironmentVariable);

        string hivePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName,
                HiveDirectoryName)
            : configured;

        HivePath = Path.GetFullPath(hivePath);
        TimestampPath = Path.Combine(HivePath, ".installed");
    }
}
