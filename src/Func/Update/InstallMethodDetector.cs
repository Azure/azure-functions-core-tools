// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IInstallMethodDetector" />
internal sealed class InstallMethodDetector(IOptions<CliEnvironmentOptions> environmentOptions) : IInstallMethodDetector
{
    private readonly CliEnvironmentOptions _environment = (environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions))).Value;

    public InstallMethod Detect()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return InstallMethod.Direct;
        }

        // Normalise separators so a single set of substring checks works on
        // both POSIX and Windows paths (npm on Windows still installs under
        // \node_modules\, choco under \chocolatey\, etc.).
        string normalized = processPath.Replace('\\', '/');

        if (Contains(normalized, "/node_modules/"))
        {
            return new InstallMethod(
                InstallMethodKind.Npm,
                "npm",
                "Reinstall Azure Functions CLI with the v5 installer at https://aka.ms/func-cli.");
        }

        // Homebrew keg-only formulas live under Cellar/; the exposed binary is
        // usually a symlink from /opt/homebrew/bin or /usr/local/bin, but
        // ProcessPath resolves to the real Cellar path on macOS.
        if (Contains(normalized, "/Cellar/")
            || Contains(normalized, "/homebrew/")
            || Contains(normalized, "/linuxbrew/"))
        {
            return new InstallMethod(
                InstallMethodKind.Homebrew,
                "Homebrew",
                "Run 'brew upgrade azure-functions-core-tools' to update.");
        }

        // Chocolatey shims live under %ChocolateyInstall%\bin\; the resolved
        // process path points into lib\azure-functions-core-tools\tools\.
        if (Contains(normalized, "/chocolatey/"))
        {
            return new InstallMethod(
                InstallMethodKind.Chocolatey,
                "Chocolatey",
                "Run 'choco upgrade azure-functions-core-tools' to update.");
        }

        // winget places packages under %LOCALAPPDATA%\Microsoft\WinGet\Packages\
        // by default; the resolved binary path contains that segment.
        if (Contains(normalized, "/WinGet/Packages/")
            || Contains(normalized, "/winget/packages/"))
        {
            return new InstallMethod(
                InstallMethodKind.Winget,
                "winget",
                "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.");
        }

        return InstallMethod.Direct;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
