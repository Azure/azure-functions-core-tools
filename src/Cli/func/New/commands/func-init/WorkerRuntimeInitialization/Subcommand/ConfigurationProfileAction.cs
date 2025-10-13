// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.New;

namespace Azure.Functions.Cli.Commands.Init;

internal class ConfigurationProfileAction : IAction
{
    private readonly IEnumerable<IConfigurationProfile> _profiles;

    public ConfigurationProfileAction(IEnumerable<IConfigurationProfile> profiles = null)
    {
        _profiles = profiles;
    }

    public string Name => "--configurationProfile";

    public string Description => "Apply a configuration profile to the project.";

    public async Task<int> Run(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var folderName = parseResult.GetValue(InitCommandParser.FolderNameArgument);
        if (!string.IsNullOrEmpty(folderName))
        {
            var folderPath = Path.Combine(Environment.CurrentDirectory, folderName);
            FileSystemHelpers.EnsureDirectory(folderPath);
            Environment.CurrentDirectory = folderPath;
        }

        var workerRuntime = parseResult.GetValue(ConfigurationProfileCommandParser.WorkerRuntimeOption);
        var profileName = parseResult.GetValue(ConfigurationProfileCommandParser.ConfigurationProfileName);
        var configurationProfile = _profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

        await configurationProfile.ApplyAsync(workerRuntime, cancellationToken);

        return 0;
    }
}
