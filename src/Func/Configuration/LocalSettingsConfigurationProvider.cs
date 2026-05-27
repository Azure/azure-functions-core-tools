// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Configuration;

internal sealed class LocalSettingsConfigurationProvider(
    DirectoryInfo projectDirectory,
    ILocalSettingsProvider localSettingsProvider) : ConfigurationProvider
{
    private readonly DirectoryInfo _projectDirectory = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    public override void Load()
    {
        LocalSettingsSnapshot localSettings = _localSettingsProvider.Get(_projectDirectory);
        Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);

        if (localSettings.WorkerRuntime is { Length: > 0 } runtime)
        {
            data[$"{StackOptions.SectionName}:runtime"] = runtime;
        }

        if (localSettings.Host?.LocalHttpPort is { } port)
        {
            data[$"{HostStartupOptions.SectionName}:{nameof(HostStartupOptions.Port)}"] = port.ToString(CultureInfo.InvariantCulture);
        }

        if (localSettings.Host?.Cors is { Length: > 0 } cors)
        {
            data[$"{HostStartupOptions.SectionName}:{nameof(HostStartupOptions.Cors)}"] = cors;
        }

        if (localSettings.Host?.CorsCredentials is { } corsCredentials)
        {
            data[$"{HostStartupOptions.SectionName}:{nameof(HostStartupOptions.CorsCredentials)}"] =
                corsCredentials.ToString(CultureInfo.InvariantCulture);
        }

        Data = data;
    }
}
