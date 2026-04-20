// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Console;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Manages host runtime installations via NuGet.
/// Creates a temporary project referencing the host package, runs dotnet publish
/// to resolve all transitive dependencies, then installs the output.
/// </summary>
public class NuGetHostManager : IHostManager
{
    /// <summary>
    /// NuGet feed URLs to search for host packages, in priority order.
    /// </summary>
    private static readonly string[] _nuGetSources =
    [
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsRelease/nuget/v3/index.json",
        "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctions/nuget/v3/index.json",
        "https://api.nuget.org/v3/index.json",
    ];

    private const string HostTargetFramework = "net8.0";

    internal const string HostRuntimeConfigFileName = "Microsoft.Azure.WebJobs.Script.WebHost.runtimeconfig.json";
    internal const string HostDepsFileName = "Microsoft.Azure.WebJobs.Script.WebHost.deps.json";

    private readonly IInteractionService _interaction;

    public NuGetHostManager(IInteractionService interaction)
    {
        _interaction = interaction;
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(CancellationToken cancellationToken = default)
    {
        var allVersions = new HashSet<NuGetVersion>();

        foreach (var sourceUrl in _nuGetSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(sourceUrl);
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

                var cache = new SourceCacheContext();
                var versions = await resource.GetAllVersionsAsync(
                    KnownHostVersions.HostPackageId,
                    cache,
                    NullLogger.Instance,
                    cancellationToken);

                foreach (var v in versions)
                {
                    allVersions.Add(v);
                }
            }
            catch
            {
                // Feed may require auth or be unreachable — skip silently
            }
        }

        return allVersions
            .Where(v => !v.IsPrerelease)
            .OrderByDescending(v => v)
            .Select(v => v.ToNormalizedString())
            .ToList();
    }

    public IReadOnlyList<InstalledHostVersion> GetInstalledVersions()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        if (!Directory.Exists(hostsDir))
        {
            return [];
        }

        var defaultVersion = GetDefaultVersion();
        var results = new List<InstalledHostVersion>();

        foreach (var dir in Directory.GetDirectories(hostsDir))
        {
            var version = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(version)) continue;

            var hostDll = Path.Combine(dir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
            if (!File.Exists(hostDll)) continue;

            results.Add(new InstalledHostVersion(
                version,
                hostDll,
                string.Equals(version, defaultVersion, StringComparison.OrdinalIgnoreCase),
                KnownHostVersions.IsVerified(version)));
        }

        return results
            .OrderByDescending(v => v.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> InstallAsync(
        string version,
        IProgress<HostInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, version);
        var hostDll = Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");

        if (File.Exists(hostDll))
        {
            _interaction.WriteMarkupLine($"[grey]Host version {version} is already installed.[/]");
            return hostDll;
        }

        if (!KnownHostVersions.IsVerified(version))
        {
            _interaction.WriteMarkupLine(
                $"[yellow]Note:[/] Host version [bold]{version}[/] has not been verified with this CLI version. " +
                $"If you encounter issues, use [green]func host install {KnownHostVersions.RecommendedVersion}[/] for a verified version.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"func-host-{version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            progress?.Report(new HostInstallProgress("Resolving dependencies", 5));

            CreateTempHostProject(tempDir, version);

            progress?.Report(new HostInstallProgress("Restoring packages", 15));

            var restoreResult = await RunDotnetAsync(
                $"restore --no-cache",
                tempDir,
                cancellationToken);

            if (!restoreResult.Success)
            {
                _interaction.WriteError($"Failed to restore host version '{version}'. The version may not exist or the NuGet feed may be unreachable.");
                if (!string.IsNullOrWhiteSpace(restoreResult.StdErr))
                {
                    _interaction.WriteError(restoreResult.StdErr);
                }
                return null;
            }

            progress?.Report(new HostInstallProgress("Publishing host runtime", 40));

            var publishDir = Path.Combine(tempDir, "publish");
            var publishResult = await RunDotnetAsync(
                $"publish -c Release -o \"{publishDir}\" --no-restore",
                tempDir,
                cancellationToken);

            if (!publishResult.Success)
            {
                _interaction.WriteError($"Failed to publish host version '{version}'.");
                if (!string.IsNullOrWhiteSpace(publishResult.StdErr))
                {
                    _interaction.WriteError(publishResult.StdErr);
                }
                return null;
            }

            var publishedHostDll = Path.Combine(publishDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
            if (!File.Exists(publishedHostDll))
            {
                _interaction.WriteError(
                    $"The host package for version '{version}' did not produce the expected host assembly. " +
                    "This may be an incompatible package version.");
                return null;
            }

            progress?.Report(new HostInstallProgress("Finalizing", 75));

            CopyHostRuntimeConfig(publishDir, version);

            var wrapperDeps = Path.Combine(publishDir, "HostInstall.deps.json");
            var hostDeps = Path.Combine(publishDir, HostDepsFileName);
            if (File.Exists(wrapperDeps) && !File.Exists(hostDeps))
            {
                File.Move(wrapperDeps, hostDeps);
            }

            foreach (var wrapper in Directory.GetFiles(publishDir, "HostInstall.*"))
            {
                if (!wrapper.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(wrapper);
                }
            }

            var workersDir = Path.Combine(publishDir, "workers");
            CopyWorkersFromNuGetCache(workersDir);

            progress?.Report(new HostInstallProgress("Installing", 85));

            Directory.CreateDirectory(hostsDir);

            if (Directory.Exists(versionDir))
            {
                Directory.Delete(versionDir, true);
            }

            Directory.Move(publishDir, versionDir);

            progress?.Report(new HostInstallProgress("Complete", 100));

            return Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _interaction.WriteError($"Failed to install host version '{version}': {ex.Message}");
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    public bool Remove(string version)
    {
        var versionDir = Path.Combine(HostResolver.GetHostsDirectory(), version);

        if (!Directory.Exists(versionDir))
        {
            _interaction.WriteError($"Host version '{version}' is not installed.");
            return false;
        }

        try
        {
            Directory.Delete(versionDir, true);

            if (string.Equals(GetDefaultVersion(), version, StringComparison.OrdinalIgnoreCase))
            {
                ClearDefaultVersion();
            }

            return true;
        }
        catch (Exception ex)
        {
            _interaction.WriteError($"Failed to remove host version '{version}': {ex.Message}");
            return false;
        }
    }

    public string? GetDefaultVersion()
    {
        var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
        if (!File.Exists(configPath)) return null;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("hostVersion", out var prop) &&
                prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch { /* malformed config */ }

        return null;
    }

    public void SetDefaultVersion(string version)
    {
        var configDir = HostResolver.GetDataDirectory();
        Directory.CreateDirectory(configDir);

        var configPath = Path.Combine(configDir, "config.json");
        var json = $"{{\n  \"hostVersion\": \"{version}\"\n}}\n";
        File.WriteAllText(configPath, json);
    }

    private void ClearDefaultVersion()
    {
        var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
        if (File.Exists(configPath))
        {
            File.WriteAllText(configPath, "{}\n");
        }
    }

    private static void CopyHostRuntimeConfig(string publishDir, string version)
    {
        var destPath = Path.Combine(publishDir, HostRuntimeConfigFileName);
        if (File.Exists(destPath)) return;

        var nugetCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages",
            KnownHostVersions.HostPackageId.ToLowerInvariant(),
            version,
            "lib", HostTargetFramework, HostRuntimeConfigFileName);

        if (File.Exists(nugetCachePath))
        {
            File.Copy(nugetCachePath, destPath);
        }
    }

    private static void CopyWorkersFromNuGetCache(string workersDir)
    {
        Directory.CreateDirectory(workersDir);

        var nugetPackagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        if (!Directory.Exists(nugetPackagesDir)) return;

        foreach (var packageDir in Directory.GetDirectories(nugetPackagesDir))
        {
            var packageName = Path.GetFileName(packageDir).ToLowerInvariant();
            if (!packageName.StartsWith("microsoft.azure.functions.") ||
                !packageName.Contains("worker"))
            {
                continue;
            }

            if (packageName.Contains("worker.sdk") ||
                packageName.Contains("worker.core") ||
                packageName.Contains("worker.grpc") ||
                packageName.Contains("worker.extensions") ||
                packageName.Contains("worker.applicationinsights") ||
                packageName.Contains("worker.itemtemplates") ||
                packageName.Contains("worker.projecttemplates"))
            {
                continue;
            }

            var versionDirs = Directory.GetDirectories(packageDir)
                .OrderByDescending(d => Path.GetFileName(d))
                .ToArray();

            foreach (var versionDir in versionDirs)
            {
                var contentWorkers = Path.Combine(versionDir, "contentFiles", "any", "any", "workers");
                if (Directory.Exists(contentWorkers))
                {
                    CopyDirectoryRecursive(contentWorkers, workersDir);
                    break;
                }

                var toolsDir = Path.Combine(versionDir, "tools");
                if (Directory.Exists(toolsDir) && File.Exists(Path.Combine(toolsDir, "worker.config.json")))
                {
                    var pythonWorkerDir = Path.Combine(workersDir, "python");
                    CopyDirectoryRecursive(toolsDir, pythonWorkerDir);
                    break;
                }
            }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }

    private static void CreateTempHostProject(string projectDir, string version)
    {
        var nugetConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="AzureFunctionsRelease" value="{_nuGetSources[0]}" />
                <add key="AzureFunctions" value="{_nuGetSources[1]}" />
                <add key="nuget.org" value="{_nuGetSources[2]}" />
              </packageSources>
            </configuration>
            """;
        File.WriteAllText(Path.Combine(projectDir, "NuGet.Config"), nugetConfig);

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>{HostTargetFramework}</TargetFramework>
                <OutputType>Library</OutputType>
                <NoWarn>$(NoWarn);NU1903;NU1901;NU1902</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{KnownHostVersions.HostPackageId}" Version="{version}" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "HostInstall.csproj"), csproj);
    }

    private static async Task<DotnetResult> RunDotnetAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new DotnetResult(process.ExitCode == 0, stdout, stderr);
    }

    private record DotnetResult(bool Success, string StdOut, string StdErr);
}
