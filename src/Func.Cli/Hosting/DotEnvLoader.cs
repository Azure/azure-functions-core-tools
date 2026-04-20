// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Loads environment variables from .env files. Supports the standard format:
/// KEY=VALUE, # comments, empty lines, and optional quoting.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Loads a .env file into the given dictionary. Existing keys are NOT overwritten
    /// (env file is lower priority than explicit config).
    /// </summary>
    public static void Load(string filePath, Dictionary<string, string> env, bool overwrite = false)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Skip export prefix (e.g., "export KEY=VALUE")
            var content = trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase)
                ? trimmed["export ".Length..].TrimStart()
                : trimmed;

            var equalsIndex = content.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = content[..equalsIndex].Trim();
            var value = content[(equalsIndex + 1)..].Trim();

            // Remove surrounding quotes if present
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (overwrite || !env.ContainsKey(key))
            {
                env[key] = value;
            }
        }
    }
}
