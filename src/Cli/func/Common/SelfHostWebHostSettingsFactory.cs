// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static ScriptApplicationHostOptions Create(string scriptPath)
        {
            return new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = Path.Combine(Path.GetTempPath(), "LogFiles", "Application", "Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets"),

                // host.json is optional. When the project has no host.json, run the host with a
                // read-only file system so it does not synthesize and write a default host.json
                // to the user's project directory (it uses an in-memory default config instead).
                IsFileSystemReadOnly = !File.Exists(Path.Combine(scriptPath, ScriptConstants.HostMetadataFileName))
            };
        }
    }
}
