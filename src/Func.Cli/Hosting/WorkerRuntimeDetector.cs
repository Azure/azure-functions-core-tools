// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Auto-detects the worker runtime from project files when FUNCTIONS_WORKER_RUNTIME
/// is not explicitly configured. Eliminates the need for local.settings.json in most cases.
/// </summary>
public static class WorkerRuntimeDetector
{
    /// <summary>
    /// Attempts to detect the worker runtime from project files in the given directory.
    /// Returns null if no runtime can be determined.
    /// </summary>
    public static string? Detect(string scriptRoot)
    {
        // .NET (isolated or in-proc)
        if (HasFile(scriptRoot, "*.csproj") || HasFile(scriptRoot, "*.fsproj"))
        {
            return "dotnet-isolated";
        }

        // Node.js
        if (File.Exists(Path.Combine(scriptRoot, "package.json")))
        {
            return DetectNodeRuntime(scriptRoot);
        }

        // Python
        if (File.Exists(Path.Combine(scriptRoot, "requirements.txt")) ||
            File.Exists(Path.Combine(scriptRoot, "pyproject.toml")))
        {
            return "python";
        }

        // Java
        if (File.Exists(Path.Combine(scriptRoot, "pom.xml")) ||
            File.Exists(Path.Combine(scriptRoot, "build.gradle")) ||
            File.Exists(Path.Combine(scriptRoot, "build.gradle.kts")))
        {
            return "java";
        }

        // PowerShell
        if (File.Exists(Path.Combine(scriptRoot, "profile.ps1")) ||
            HasFile(scriptRoot, "*/function.json") && HasFile(scriptRoot, "*/run.ps1"))
        {
            return "powershell";
        }

        return null;
    }

    private static string DetectNodeRuntime(string scriptRoot)
    {
        // Check if TypeScript is configured
        if (File.Exists(Path.Combine(scriptRoot, "tsconfig.json")))
        {
            return "node";
        }

        return "node";
    }

    private static bool HasFile(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern).Any();
        }
        catch (IOException)
        {
            return false;
        }
    }
}
