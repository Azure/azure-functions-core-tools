// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Configuration;

internal sealed class CliConfigurationSourceBuilder(ILocalSettingsProvider localSettingsProvider)
{
    private readonly ILocalSettingsProvider _localSettingsProvider =
        localSettingsProvider ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    public IConfigurationRoot Build(DirectoryInfo projectDirectory)
    {
        var builder = new ConfigurationBuilder();
        AddSources(builder, projectDirectory);
        return builder.Build();
    }

    public void AddSources(IConfigurationBuilder builder, DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        builder.AddEnvironmentVariables(prefix: Constants.EnvironmentVariablePrefix);
        builder.AddJsonFile(GetGlobalConfigPath(), optional: true, reloadOnChange: false);
        builder.Add(new LocalSettingsConfigurationSource(projectDirectory, _localSettingsProvider));
        builder.AddJsonFile(GetProjectConfigPath(projectDirectory), optional: true, reloadOnChange: false);
    }

    private static string GetGlobalConfigPath()
    {
        string home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName);

        return Path.Combine(home, CliConfigurationNames.ConfigFileName);
    }

    private static string GetProjectConfigPath(DirectoryInfo workingDirectory)
        => Path.Combine(
            workingDirectory.FullName,
            CliConfigurationNames.ProjectConfigFolderName,
            CliConfigurationNames.ConfigFileName);
}
