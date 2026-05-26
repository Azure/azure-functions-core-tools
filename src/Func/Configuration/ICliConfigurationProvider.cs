// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Configuration;

/// <summary>
/// Provides scoped CLI configuration roots.
/// </summary>
internal interface ICliConfigurationProvider
{
    public IConfiguration GetUserConfiguration();

    public IConfiguration GetProjectConfiguration(DirectoryInfo projectDirectory);

    public IConfiguration GetEffectiveConfiguration(DirectoryInfo projectDirectory);
}
