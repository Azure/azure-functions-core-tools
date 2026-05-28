// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Default implementation of <see cref="IItemTemplateHivePathProvider"/>.
/// Resolves the hive path from
/// <c>FUNC_CLI_DOTNET_ITEM_TEMPLATE_HIVE</c> when set, falling back to
/// <c>~/.azure-functions/dotnet-item-template-hive</c>.
/// </summary>
internal sealed class ItemTemplateHivePathProvider : IItemTemplateHivePathProvider
{
    internal const string HivePathEnvironmentVariable = "FUNC_CLI_DOTNET_ITEM_TEMPLATE_HIVE";
    private const string HiveDirectoryName = "dotnet-item-template-hive";

    public string HivePath { get; }

    public string TimestampPath { get; }

    public ItemTemplateHivePathProvider()
    {
        string? configured = Environment.GetEnvironmentVariable(HivePathEnvironmentVariable);

        string hivePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".azure-functions",
                HiveDirectoryName)
            : configured;

        HivePath = Path.GetFullPath(hivePath);
        TimestampPath = Path.Combine(HivePath, ".installed");
    }
}
