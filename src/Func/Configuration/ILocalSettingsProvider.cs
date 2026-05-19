// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal interface ILocalSettingsProvider
{
    /// <summary>
    /// Reads and caches <c>local.settings.json</c> for the specified project directory.
    /// </summary>
    public LocalSettingsSnapshot Get(DirectoryInfo projectDirectory);
}
