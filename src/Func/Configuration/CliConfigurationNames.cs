// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal static class CliConfigurationNames
{
    public const string ProjectConfigFolderName = ".func";
    public const string ConfigFileName = "config.json";
    public const string LocalSettingsFileName = "local.settings.json";
    public const string LocalSettingsValuesSectionName = "Values";
    public const string LocalSettingsHostSectionName = "Host";
    public const string WorkerRuntimeSettingName = "FUNCTIONS_WORKER_RUNTIME";
}
