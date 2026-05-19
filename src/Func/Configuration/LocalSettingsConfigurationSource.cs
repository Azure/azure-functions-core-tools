// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Configuration;

internal sealed class LocalSettingsConfigurationSource(
    DirectoryInfo projectDirectory,
    ILocalSettingsProvider localSettingsProvider) : IConfigurationSource
{
    private readonly DirectoryInfo _projectDirectory = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new LocalSettingsConfigurationProvider(_projectDirectory, _localSettingsProvider);
}
