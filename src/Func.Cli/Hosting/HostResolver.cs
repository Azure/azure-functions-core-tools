// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Text.Json;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Resolves the host runtime DLL path using a well-defined precedence chain.
/// Supports side-by-side host versions and auto-detection of installed hosts.
/// </summary>
public class HostResolver : IHostResolver
{
    private const string HostAssemblyName = "Microsoft.Azure.WebJobs.Script.WebHost.dll";
    private const string HostPathOverrideEnvVar = "FUNC_HOST_PATH";
    private const string ProjectConfigFileName = ".func-config.json";
    private const string UserConfigFileName = "config.json";

    private readonly IInteractionService _interaction;

    public HostResolver(IInteractionService interaction)
    {
        _interaction = interaction;
    }

    /// <summary>
    /// Resolves the host DLL path using the following precedence:
    /// 1. Explicit --host-version CLI argument
    /// 2. FUNC_HOST_PATH environment variable (dev/testing override)
    /// 3. Per-project config (.func-config.json in script root)
    /// 4. User default (~/.azure-functions-cli/config.json)
    /// 5. Bundled host (next to CLI binary)
    /// 6. Latest installed version in ~/.azure-functions-cli/hosts/
    /// 7. Well-known system install paths (e.g., v4 Core Tools)
    /// </summary>
    public HostResolution? Resolve(string scriptRoot, string? requestedVersion)
    {
        // 1. Explicit version requested via --host-version
        if (!string.IsNullOrEmpty(requestedVersion))
        {
            return ResolveFromInstalledVersion(requestedVersion, "CLI argument --host-version");
        }

        // 2. FUNC_HOST_PATH environment variable (true override for dev/testing)
        var envPath = Environment.GetEnvironmentVariable(HostPathOverrideEnvVar);
        if (!string.IsNullOrEmpty(envPath))
        {
            if (File.Exists(envPath))
            {
                return new HostResolution(envPath, DetectVersion(envPath), $"environment variable {HostPathOverrideEnvVar}");
            }

            _interaction.WriteError($"Host path override '{envPath}' (from {HostPathOverrideEnvVar}) does not exist.");
            return null;
        }

        // 3. Per-project config
        var projectVersion = ReadProjectConfig(scriptRoot);
        if (!string.IsNullOrEmpty(projectVersion))
        {
            return ResolveFromInstalledVersion(projectVersion, $"project config ({ProjectConfigFileName})");
        }

        // 4. User default
        var userDefaultVersion = ReadUserDefaultVersion();
        if (!string.IsNullOrEmpty(userDefaultVersion))
        {
            return ResolveFromInstalledVersion(userDefaultVersion, "user default");
        }

        // 5. Bundled host (next to CLI binary)
        var bundledPath = Path.Combine(AppContext.BaseDirectory, HostAssemblyName);
        if (File.Exists(bundledPath))
        {
            return new HostResolution(bundledPath, DetectVersion(bundledPath), "bundled with CLI");
        }

        // 6. Latest installed version in hosts directory
        var latestInstalled = FindLatestInstalledVersion();
        if (latestInstalled is not null)
        {
            return latestInstalled;
        }

        // 7. Well-known system install paths (e.g., existing Core Tools v4)
        var systemPath = FindFromSystemPaths();
        if (systemPath is not null)
        {
            return systemPath;
        }

        // Nothing found — provide guidance
        _interaction.WriteError(
            "Unable to find the Azure Functions host runtime.\n" +
            "To resolve this, try one of the following:\n" +
            $"  • Install a host version: func host install {KnownHostVersions.RecommendedVersion}\n" +
            "  • Set the host path manually: export FUNC_HOST_PATH=/path/to/Microsoft.Azure.WebJobs.Script.WebHost.dll\n" +
            "  • Reinstall Azure Functions CLI with a bundled host.");

        return null;
    }

    /// <summary>
    /// Returns a list of installed host versions.
    /// </summary>
    public IReadOnlyList<string> GetInstalledVersions()
    {
        var hostsDir = GetHostsDirectory();
        if (!Directory.Exists(hostsDir))
        {
            return [];
        }

        return Directory.GetDirectories(hostsDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private HostResolution? ResolveFromInstalledVersion(string version, string source)
    {
        var versionDir = Path.Combine(GetHostsDirectory(), version);
        var hostPath = Path.Combine(versionDir, HostAssemblyName);

        if (File.Exists(hostPath))
        {
            return new HostResolution(hostPath, version, source);
        }

        if (Directory.Exists(versionDir))
        {
            _interaction.WriteError(
                $"Host version '{version}' (from {source}) appears to be an incomplete installation. " +
                $"Try reinstalling: func host install {version}");
        }
        else
        {
            _interaction.WriteError(
                $"Host version '{version}' (from {source}) is not installed. " +
                $"Install it with: func host install {version}");

            var installed = GetInstalledVersions();
            if (installed.Count > 0)
            {
                _interaction.WriteMarkupLine($"[yellow]Installed versions:[/] {string.Join(", ", installed)}");
            }
        }

        return null;
    }

    private string? ReadProjectConfig(string scriptRoot)
    {
        var configPath = Path.Combine(scriptRoot, ProjectConfigFileName);
        return ReadVersionFromJsonFile(configPath, "hostVersion");
    }

    private string? ReadUserDefaultVersion()
    {
        var configPath = Path.Combine(GetDataDirectory(), UserConfigFileName);
        return ReadVersionFromJsonFile(configPath, "hostVersion");
    }

    private static string? ReadVersionFromJsonFile(string filePath, string propertyName)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // Malformed config — skip
        }

        return null;
    }

    private HostResolution? FindLatestInstalledVersion()
    {
        var versions = GetInstalledVersions();
        if (versions.Count == 0)
        {
            return null;
        }

        var latest = versions[0]; // already sorted descending
        var hostPath = Path.Combine(GetHostsDirectory(), latest, HostAssemblyName);

        if (File.Exists(hostPath))
        {
            return new HostResolution(hostPath, latest, "latest installed version");
        }

        return null;
    }

    private HostResolution? FindFromSystemPaths()
    {
        foreach (var path in GetWellKnownHostPaths())
        {
            if (File.Exists(path))
            {
                _interaction.WriteMarkupLine(
                    "[yellow]⚠ Using system-installed Azure Functions host (v4).[/]\n" +
                    "[yellow]  This is deprecated in Core Tools v5. Install a managed host version:[/]\n" +
                    $"[yellow]  func host install {KnownHostVersions.RecommendedVersion}[/]\n");
                return new HostResolution(path, DetectVersion(path), "system installation");
            }
        }

        return null;
    }

    /// <summary>
    /// Returns well-known paths where Azure Functions Core Tools may be installed.
    /// </summary>
    internal static IEnumerable<string> GetWellKnownHostPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return Path.Combine(programFiles, "Microsoft", "Azure Functions Core Tools", HostAssemblyName);
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                yield return Path.Combine(appData, "npm", "node_modules", "azure-functions-core-tools", "bin", HostAssemblyName);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine("/opt/homebrew/opt/azure-functions-core-tools@4/", HostAssemblyName);
            yield return Path.Combine("/usr/local/opt/azure-functions-core-tools@4/", HostAssemblyName);
            yield return Path.Combine("/usr/local/lib/node_modules/azure-functions-core-tools/bin", HostAssemblyName);
        }
        else
        {
            yield return Path.Combine("/usr/lib/azure-functions-core-tools-4", HostAssemblyName);
            yield return Path.Combine("/usr/local/lib/node_modules/azure-functions-core-tools/bin", HostAssemblyName);
        }
    }

    private static string? DetectVersion(string hostDllPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(hostDllPath);
            if (dir is null) return null;

            var versionFile = Path.Combine(dir, "version.txt");
            if (File.Exists(versionFile))
            {
                var version = File.ReadAllText(versionFile).Trim();
                if (!string.IsNullOrEmpty(version)) return version;
            }

            var assemblyInfo = System.Reflection.AssemblyName.GetAssemblyName(hostDllPath);
            return assemblyInfo.Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the base data directory for CLI state (~/.azure-functions-cli or platform equivalent).
    /// </summary>
    internal static string GetDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AzureFunctionsCli");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".azure-functions-cli");
        }

        // Linux: XDG data directory
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgData))
        {
            return Path.Combine(xdgData, "azure-functions-cli");
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".local", "share", "azure-functions-cli");
    }

    /// <summary>
    /// Returns the hosts directory where side-by-side host versions are stored.
    /// </summary>
    internal static string GetHostsDirectory()
    {
        return Path.Combine(GetDataDirectory(), "hosts");
    }
}
