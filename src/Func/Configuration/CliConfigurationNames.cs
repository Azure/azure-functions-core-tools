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

    // Schema keys for .func/config.json. Lowercase to match the file's
    // existing "profiles" / "defaultProfile" convention.
    public const string StackSectionName = "stack";
    public const string StackRuntimeKey = "runtime";
    public const string StackLanguageKey = "language";
    public const string ProfilesKey = "profiles";
    public const string DefaultProfileKey = "defaultProfile";
}
