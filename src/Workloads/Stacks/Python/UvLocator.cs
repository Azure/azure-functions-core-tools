// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Finds the uv executable without starting a process.
/// </summary>
internal static class UvLocator
{
    // `uv run` sets UV to its own executable path for child processes, which finds uv
    // under `uv run func run` even when uv's directory is not on PATH.
    internal const string UvEnvVar = "UV";

    public static string? Find()
        => Find(Environment.GetEnvironmentVariable(UvEnvVar), Environment.GetEnvironmentVariable("PATH"));

    public static string? Find(string? uvEnv, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(uvEnv) && File.Exists(uvEnv))
        {
            return Path.GetFullPath(uvEnv);
        }

        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        string fileName = OperatingSystem.IsWindows() ? "uv.exe" : "uv";
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim('"'), fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
