// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Performs install / uninstall of a workload package against the local
/// filesystem and the global manifest. Accepts either a <c>.nupkg</c> file
/// or a directory whose layout matches an unpacked NuGet package (<c>*.nuspec</c>
/// at the root, assemblies under <c>lib/&lt;tfm&gt;/</c>). Workload identity
/// (id / display name / description) is read from the <see cref="IWorkload"/>
/// implementation in the package — authors don't write a side-car manifest.
/// </summary>
internal static class WorkloadInstaller
{
    /// <summary>Result of a successful install — caller uses for output.</summary>
    public readonly record struct InstallResult(GlobalManifestEntry Entry, bool Replaced);

    /// <summary>Installs from a .nupkg file or a directory.</summary>
    public static InstallResult Install(string source)
    {
        if (Directory.Exists(source))
        {
            return InstallFromDirectory(source);
        }

        if (File.Exists(source) && string.Equals(Path.GetExtension(source), ".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return InstallFromNupkg(source);
        }

        throw new GracefulException(
            $"'{source}' is not a directory or .nupkg file.",
            isUserError: true);
    }

    /// <summary>Removes a package from the global manifest. Returns true if it was present.</summary>
    public static bool Uninstall(string packageId, bool deleteFiles)
    {
        var manifest = GlobalManifestStore.Read();
        var existing = manifest.Workloads.FirstOrDefault(w =>
            string.Equals(w.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return false;
        }

        manifest.Workloads.Remove(existing);
        GlobalManifestStore.Write(manifest);

        if (deleteFiles && Directory.Exists(existing.InstallPath))
        {
            try
            {
                Directory.Delete(existing.InstallPath, recursive: true);
            }
            catch (IOException)
            {
                // Best effort — manifest entry is gone, files are stale but harmless.
            }
        }

        return true;
    }

    private static InstallResult InstallFromDirectory(string sourceDir)
    {
        var nuspec = NuspecReader.ReadFromDirectory(sourceDir);

        // Stage to a deterministic install path under workloads root. We
        // don't yet know the packageId — the probe needs the staged dir to
        // load the assembly. Use a temp-named dir, probe, then move into
        // place.
        var staging = Path.Combine(WorkloadPaths.WorkloadsRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(sourceDir, staging);
            return ProbeAndCommit(staging, nuspec);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    private static InstallResult InstallFromNupkg(string nupkgPath)
    {
        NuspecReader.NuspecData nuspec;
        var staging = Path.Combine(WorkloadPaths.WorkloadsRoot, ".staging-" + Guid.NewGuid().ToString("N"));

        using (var archive = ZipFile.OpenRead(nupkgPath))
        {
            nuspec = NuspecReader.ReadFromArchive(archive, nupkgPath);
            Directory.CreateDirectory(staging);
            try
            {
                ExtractArchive(archive, staging);
            }
            catch
            {
                TryDelete(staging);
                throw;
            }
        }

        try
        {
            return ProbeAndCommit(staging, nuspec);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    private static InstallResult ProbeAndCommit(string stagingPath, NuspecReader.NuspecData nuspec)
    {
        var probe = WorkloadProbe.Probe(stagingPath);
        var workload = probe.Instance;

        // Code is the source of truth for workload identity and version.
        // The .nuspec only contributes aliases via <tags>.
        var installPath = WorkloadPaths.GetInstallDirectory(workload.PackageId, workload.PackageVersion);

        // Move staging into the final id+version slot, replacing any prior install.
        if (Directory.Exists(installPath))
        {
            Directory.Delete(installPath, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
        Directory.Move(stagingPath, installPath);

        var entry = new GlobalManifestEntry
        {
            PackageId = workload.PackageId,
            DisplayName = workload.DisplayName,
            Description = workload.Description,
            Version = workload.PackageVersion,
            Type = workload.Type,
            Aliases = nuspec.Tags,
            InstallPath = installPath,
            EntryPoint = new EntryPointSpec
            {
                Assembly = probe.AssemblyRelativePath,
                Type = probe.TypeFullName,
            },
        };

        var global = GlobalManifestStore.Read();
        var existing = global.Workloads.FirstOrDefault(w =>
            string.Equals(w.PackageId, entry.PackageId, StringComparison.OrdinalIgnoreCase));

        var replaced = existing is not null;
        if (replaced)
        {
            global.Workloads.Remove(existing!);
        }

        global.Workloads.Add(entry);
        GlobalManifestStore.Write(global);

        return new InstallResult(entry, replaced);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void ExtractArchive(ZipArchive archive, string destination)
    {
        var rootFull = Path.GetFullPath(destination);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
            {
                throw new GracefulException(
                    $"Refusing to extract '{entry.FullName}' outside the install directory.",
                    isUserError: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
