// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Hosting.AppStacks;

internal sealed class DefaultAppStackProvider : IAppStackProvider
{
    public ValueTask<string> GetStackNameAsync(WorkingDirectory workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo directory = workingDirectory.Info;
        if (HasAnyFile(directory, ["*.csproj", "*.fsproj", "*.vbproj", "*.sln", "*.slnx"]))
        {
            return ValueTask.FromResult(".NET");
        }

        if (HasAnyFile(directory, ["requirements.txt", "*.py"]))
        {
            return ValueTask.FromResult("Python");
        }

        if (HasAnyFile(directory, ["pom.xml", "build.gradle", "build.gradle.kts", "*.java"]))
        {
            return ValueTask.FromResult("Java");
        }

        if (HasAnyFile(directory, ["profile.ps1", "requirements.psd1", "*.ps1"]))
        {
            return ValueTask.FromResult("PowerShell");
        }

        if (HasAnyFile(directory, ["package.json", "*.js", "*.ts"]))
        {
            return ValueTask.FromResult("Node.js");
        }

        return ValueTask.FromResult("unknown");
    }

    private static bool HasAnyFile(DirectoryInfo directory, IReadOnlyList<string> searchPatterns)
    {
        foreach (string pattern in searchPatterns)
        {
            if (directory.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }

        return false;
    }
}
