// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IInstallMethodDetector" />
internal sealed class InstallMethodDetector(
    IOptions<CliEnvironmentOptions> environmentOptions,
    IProcessEnvironment processEnvironment,
    IFileSystem fileSystem) : IInstallMethodDetector
{
    internal const string InstallDirectoryEnvironmentVariable = "FUNC_CLI_INSTALL_DIR";

    private readonly CliEnvironmentOptions _environment = (environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions))).Value;
    private readonly IProcessEnvironment _processEnvironment = processEnvironment ?? throw new ArgumentNullException(nameof(processEnvironment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public InstallMethod Detect()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw UnknownInstallation(processPath);
        }

        string normalized = Canonicalize(processPath);

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
        if (IsHomebrewInstallPath(normalized))
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
        if (installDirectory is not null && IsUnderDirectory(normalized, Canonicalize(installDirectory)))
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
        StringComparison comparison = IsWindowsPath(path) && IsWindowsPath(directory)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }

    private string Canonicalize(string path)
    {
        try
        {
            return Normalize(_fileSystem.GetCanonicalPath(path));
        }
        catch (ArgumentException)
        {
            throw UnknownInstallation(path);
        }
        catch (IOException)
        {
            throw UnknownInstallation(path);
        }
        catch (UnauthorizedAccessException)
        {
            throw UnknownInstallation(path);
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool IsWindowsPath(string path) =>
        path.StartsWith("//", StringComparison.Ordinal)
        || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/');

    private static bool IsHomebrewInstallPath(string path)
    {
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !segments[^1].Equals("func", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string formula = segments[^3];
        return segments[^4].Equals("Cellar", StringComparison.OrdinalIgnoreCase)
            && (formula.Equals("azure-functions-core-tools", StringComparison.OrdinalIgnoreCase)
                || formula.StartsWith("azure-functions-core-tools@", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static GracefulException UnknownInstallation(string? processPath) =>
        new(
            $"Cannot update the Azure Functions CLI installation at '{processPath ?? "unknown"}' in place. " +
            "Reinstall it with the v5 installer at https://aka.ms/func-cli, or use the package manager that installed it.",
            isUserError: true);
}
