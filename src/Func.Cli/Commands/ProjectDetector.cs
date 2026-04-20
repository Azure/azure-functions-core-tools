// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Shared utility for detecting the worker runtime and language of a Functions project.
/// </summary>
internal static class ProjectDetector
{
    /// <summary>
    /// Detects the worker runtime and language from the project files in the given directory.
    /// </summary>
    public static (string? Runtime, string? Language) DetectRuntimeAndLanguage(string directory)
    {
        // Check for .csproj → dotnet C#
        if (Directory.EnumerateFiles(directory, "*.csproj").Any())
        {
            return ("dotnet", "C#");
        }

        // Check for .fsproj → dotnet F#
        if (Directory.EnumerateFiles(directory, "*.fsproj").Any())
        {
            return ("dotnet", "F#");
        }

        // Check for package.json → node
        if (File.Exists(Path.Combine(directory, "package.json")))
        {
            return ("node", null);
        }

        // Check for requirements.txt or pyproject.toml → python
        if (File.Exists(Path.Combine(directory, "requirements.txt"))
            || File.Exists(Path.Combine(directory, "pyproject.toml")))
        {
            return ("python", null);
        }

        // Check for pom.xml or build.gradle → java
        if (File.Exists(Path.Combine(directory, "pom.xml"))
            || File.Exists(Path.Combine(directory, "build.gradle")))
        {
            return ("java", null);
        }

        // Check for profile.ps1 → powershell
        if (File.Exists(Path.Combine(directory, "profile.ps1")))
        {
            return ("powershell", null);
        }

        return (null, null);
    }

    /// <summary>
    /// Detects just the worker runtime from the project files in the given directory.
    /// </summary>
    public static string? DetectRuntime(string directory)
        => DetectRuntimeAndLanguage(directory).Runtime;
}
