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
    private const string HomebrewFormula = "azure-functions-core-tools";

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

        string canonicalPath = Canonicalize(processPath);
        string normalized = Normalize(canonicalPath);

        if (ContainsPathMarker(normalized, "/node_modules/"))
        {
            return new InstallMethod(
                InstallMethodKind.Npm,
                "npm",
                "Reinstall Azure Functions CLI with the v5 installer at https://aka.ms/func-cli.",
                canonicalPath);
        }

        // Homebrew keg-only formulas live under Cellar/; the exposed binary is
        // usually a symlink from /opt/homebrew/bin or /usr/local/bin, which
        // canonicalization resolves to the real Cellar path.
        string? homebrewFormula = IsWindowsPath(normalized) ? null : GetHomebrewFormula(normalized);
        if (homebrewFormula is not null)
        {
            return new InstallMethod(
                InstallMethodKind.Homebrew,
                "Homebrew",
                $"Run 'brew upgrade {homebrewFormula}' to update.",
                canonicalPath);
        }

        // winget places packages under %LOCALAPPDATA%\Microsoft\WinGet\Packages\
        // by default; the resolved binary path contains that segment.
        if (IsWindowsManagedInstallPath(normalized))
        {
            return new InstallMethod(
                InstallMethodKind.Winget,
                "winget",
                "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.",
                canonicalPath);
        }

        string? installDirectory = GetInstallDirectory(normalized);
        if (installDirectory is not null && IsUnderDirectory(normalized, Normalize(Canonicalize(installDirectory))))
        {
            return InstallMethod.Direct(canonicalPath);
        }

        throw UnknownInstallation(processPath);
    }

    private string? GetInstallDirectory(string processPath)
    {
        string? configured = _processEnvironment.Get(InstallDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        bool isWindowsPath = IsWindowsPath(processPath);
        string primaryHomeVariable = isWindowsPath ? "USERPROFILE" : "HOME";
        string fallbackHomeVariable = isWindowsPath ? "HOME" : "USERPROFILE";
        string? home = _processEnvironment.Get(primaryHomeVariable);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = _processEnvironment.Get(fallbackHomeVariable);
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
            return _fileSystem.GetCanonicalPath(path);
        }
        catch (ArgumentException ex)
        {
            throw UnknownInstallation(path, ex);
        }
        catch (IOException ex)
        {
            throw UnknownInstallation(path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw UnknownInstallation(path, ex);
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool IsWindowsPath(string path) =>
        path.StartsWith("//", StringComparison.Ordinal)
        || IsWindowsDrivePath(path);

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';

    private static bool IsWindowsManagedInstallPath(string path) =>
        IsWindowsDrivePath(path)
        && (ContainsPathMarker(path, "/Microsoft/WinGet/Packages/")
            || ContainsPathMarker(path, "/Microsoft/WindowsApps/")
            || ContainsPathMarker(path, "/Program Files/WindowsApps/")
            || ContainsPathMarker(path, "/Program Files/Microsoft/Azure Functions Core Tools/"));

    private static string? GetHomebrewFormula(string path)
    {
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !segments[^1].Equals("func", StringComparison.Ordinal))
        {
            return null;
        }

        string formula = segments[^3];
        if (!segments[^4].Equals("Cellar", StringComparison.Ordinal))
        {
            return null;
        }

        if (formula.Equals(HomebrewFormula, StringComparison.Ordinal))
        {
            return HomebrewFormula;
        }

        string versionedPrefix = HomebrewFormula + "@";
        string version = formula.StartsWith(versionedPrefix, StringComparison.Ordinal)
            ? formula[versionedPrefix.Length..]
            : string.Empty;
        return version.Length > 0 && version.All(char.IsAsciiDigit)
            ? versionedPrefix + version
            : null;
    }

    private static bool ContainsPathMarker(string path, string marker) =>
        path.Contains(
            marker,
            IsWindowsPath(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static InstallMethodDetectionException UnknownInstallation(string? processPath, Exception? innerException = null) =>
        new(
            $"Cannot update the Azure Functions CLI installation at '{processPath ?? "unknown"}' in place. " +
            "Reinstall it with the v5 installer at https://aka.ms/func-cli, or use the package manager that installed it.",
            innerException);
}
