// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Default implementation that resolves the template hive path, honoring the
/// <c>FUNC_CLI_WORKLOADS_HOME</c> environment variable when set, and falling
/// back to <c>~/.azure-functions/dotnet-template-hive</c>.
/// </summary>
internal sealed class TemplateHivePathProvider : ITemplateHivePathProvider
{
    private const string WorkloadsHomeEnvVar = "FUNC_CLI_WORKLOADS_HOME";
    private const string HiveDirectoryName = "dotnet-template-hive";

    public string HivePath { get; }
    public string TimestampPath { get; }

    public TemplateHivePathProvider()
    {
        string? workloadsHome = Environment.GetEnvironmentVariable(WorkloadsHomeEnvVar);

        string basePath = !string.IsNullOrEmpty(workloadsHome)
            ? workloadsHome
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName);

        HivePath = Path.Combine(basePath, HiveDirectoryName);
        TimestampPath = Path.Combine(HivePath, ".installed");
    }
}
