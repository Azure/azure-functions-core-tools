// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IInstallMethodDetector" />
internal sealed class InstallMethodDetector(
    IOptions<CliEnvironmentOptions> environmentOptions,
    IProcessEnvironment processEnvironment) : IInstallMethodDetector
{
    internal const string InstallDirectoryEnvironmentVariable = "FUNC_CLI_INSTALL_DIR";

    private readonly CliEnvironmentOptions _environment = (environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions))).Value;
    private readonly IProcessEnvironment _processEnvironment = processEnvironment ?? throw new ArgumentNullException(nameof(processEnvironment));

    public InstallMethod Detect()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw UnknownInstallation(processPath);
        }

        string normalized = Normalize(processPath);

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
            || Contains(normalized, "/winget/packages/")
            || Contains(normalized, "/WindowsApps/")
            || Contains(normalized, "/Program Files/Microsoft/Azure Functions Core Tools/"))
        {
            return new InstallMethod(
                InstallMethodKind.Winget,
                "winget",
                "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.");
        }

        string? installDirectory = GetInstallDirectory();
        if (installDirectory is not null && IsUnderDirectory(normalized, Normalize(installDirectory)))
        {
            return InstallMethod.Direct;
        }

        throw UnknownInstallation(processPath);
    }

    private string? GetInstallDirectory()
    {
        string? configured = _processEnvironment.Get(InstallDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string? home = _processEnvironment.Get("USERPROFILE");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = _processEnvironment.Get("HOME");
        }

        return string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, ".azure-functions");
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string prefix = directory.TrimEnd('/') + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static GracefulException UnknownInstallation(string? processPath) =>
        new(
            $"Cannot update the Azure Functions CLI installation at '{processPath ?? "unknown"}' in place. " +
            "Reinstall it with the v5 installer at https://aka.ms/func-cli, or use the package manager that installed it.",
            isUserError: true);
}
