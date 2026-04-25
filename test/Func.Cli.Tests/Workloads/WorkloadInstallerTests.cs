// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Reflection;
using System.Text;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

/// <summary>
/// End-to-end tests for <see cref="WorkloadInstaller"/>. Each test redirects
/// <c>FUNC_CLI_HOME</c> to a temp directory so installs/uninstalls don't
/// touch the real user profile. Fixtures are built around the real
/// <c>Azure.Functions.Cli.Workload.Dotnet</c> assembly produced by the
/// sample project so the installer's IWorkload-discovery path runs.
/// </summary>
public class WorkloadInstallerTests : IDisposable
{
    private const string SampleAssemblyName = "Azure.Functions.Cli.Workload.Dotnet";
    private const string SampleAssemblyFile = SampleAssemblyName + ".dll";
    private const string SamplePackageId = "Azure.Functions.Cli.Workload.Dotnet";

    private readonly string _tempHome;
    private readonly string? _previousHome;

    public WorkloadInstallerTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "func-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);
        _previousHome = Environment.GetEnvironmentVariable("FUNC_CLI_HOME");
        Environment.SetEnvironmentVariable("FUNC_CLI_HOME", _tempHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FUNC_CLI_HOME", _previousHome);
        try
        {
            Directory.Delete(_tempHome, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public void Install_FromDirectory_PopulatesManifestFromWorkloadType()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        BuildSampleLayoutOnDisk(sourceDir, tags: "dotnet csharp fsharp");

        var result = WorkloadInstaller.Install(sourceDir);

        Assert.False(result.Replaced);
        Assert.Equal(SamplePackageId, result.Entry.PackageId);
        // Version comes from IWorkload.PackageVersion on the sample, not the .nuspec.
        Assert.Equal("1.0.0", result.Entry.Version);
        Assert.Equal(WorkloadType.Stack, result.Entry.Type);
        Assert.Equal(new[] { "dotnet", "csharp", "fsharp" }, result.Entry.Aliases);
        Assert.Equal($"lib/net10.0/{SampleAssemblyFile}", result.Entry.EntryPoint.Assembly);
        Assert.False(string.IsNullOrWhiteSpace(result.Entry.DisplayName));

        var installPath = WorkloadPaths.GetInstallDirectory(SamplePackageId, "1.0.0");
        Assert.True(File.Exists(Path.Combine(installPath, "lib", "net10.0", SampleAssemblyFile)));

        var manifest = GlobalManifestStore.Read();
        var entry = Assert.Single(manifest.Workloads);
        Assert.Equal(installPath, entry.InstallPath);
    }

    [Fact]
    public void Install_SamePackageTwice_ReplacesExistingEntry()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        BuildSampleLayoutOnDisk(sourceDir, tags: "dotnet");
        WorkloadInstaller.Install(sourceDir);

        BuildSampleLayoutOnDisk(sourceDir, tags: "dotnet");
        var result = WorkloadInstaller.Install(sourceDir);

        Assert.True(result.Replaced);
        Assert.Single(GlobalManifestStore.Read().Workloads);
    }

    [Fact]
    public void Install_FromNupkg_ExtractsAndRegisters()
    {
        var nupkg = Path.Combine(_tempHome, "sample.nupkg");
        BuildSampleNupkg(nupkg, tags: "dotnet");

        var result = WorkloadInstaller.Install(nupkg);

        Assert.Equal(SamplePackageId, result.Entry.PackageId);
        Assert.Equal(new[] { "dotnet" }, result.Entry.Aliases);

        var installPath = WorkloadPaths.GetInstallDirectory(SamplePackageId, "1.0.0");
        Assert.True(File.Exists(Path.Combine(installPath, "lib", "net10.0", SampleAssemblyFile)));
    }

    [Fact]
    public void Install_DirectoryWithoutNuspec_Throws()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "lib", "net10.0"));
        File.WriteAllBytes(Path.Combine(sourceDir, "lib", "net10.0", "noop.dll"), new byte[] { 0 });

        Assert.Throws<Common.GracefulException>(() => WorkloadInstaller.Install(sourceDir));
    }

    [Fact]
    public void Install_NoWorkloadType_Throws()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "lib", "net10.0"));
        File.WriteAllBytes(Path.Combine(sourceDir, "lib", "net10.0", "junk.dll"), new byte[] { 0 });
        WriteNuspec(Path.Combine(sourceDir, "Sample.nuspec"), id: "Sample", tags: "");

        Assert.Throws<Common.GracefulException>(() => WorkloadInstaller.Install(sourceDir));
    }

    [Fact]
    public void Install_UnknownSource_Throws()
    {
        Assert.Throws<Common.GracefulException>(() =>
            WorkloadInstaller.Install(Path.Combine(_tempHome, "does-not-exist")));
    }

    [Fact]
    public void Uninstall_RemovesEntryAndDeletesFiles()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        BuildSampleLayoutOnDisk(sourceDir, tags: "dotnet");
        WorkloadInstaller.Install(sourceDir);
        var installPath = WorkloadPaths.GetInstallDirectory(SamplePackageId, "1.0.0");

        var removed = WorkloadInstaller.Uninstall(SamplePackageId, deleteFiles: true);

        Assert.True(removed);
        Assert.Empty(GlobalManifestStore.Read().Workloads);
        Assert.False(Directory.Exists(installPath));
    }

    [Fact]
    public void Uninstall_KeepFiles_RemovesManifestOnly()
    {
        var sourceDir = Path.Combine(_tempHome, "src");
        BuildSampleLayoutOnDisk(sourceDir, tags: "dotnet");
        WorkloadInstaller.Install(sourceDir);
        var installPath = WorkloadPaths.GetInstallDirectory(SamplePackageId, "1.0.0");

        var removed = WorkloadInstaller.Uninstall(SamplePackageId, deleteFiles: false);

        Assert.True(removed);
        Assert.Empty(GlobalManifestStore.Read().Workloads);
        Assert.True(Directory.Exists(installPath));
    }

    [Fact]
    public void Uninstall_UnknownPackage_ReturnsFalse()
    {
        var removed = WorkloadInstaller.Uninstall("Bogus.Package", deleteFiles: false);
        Assert.False(removed);
    }

    /// <summary>Returns the on-disk path to the built sample workload assembly.</summary>
    private static string LocateSampleAssembly()
    {
        var path = typeof(global::Azure.Functions.Cli.Workload.Dotnet.DotnetWorkload).Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Could not locate built '{SampleAssemblyFile}' for tests.");
        }

        return path;
    }

    private static void BuildSampleLayoutOnDisk(string dir, string tags)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        var libDir = Path.Combine(dir, "lib", "net10.0");
        Directory.CreateDirectory(libDir);
        File.Copy(LocateSampleAssembly(), Path.Combine(libDir, SampleAssemblyFile), overwrite: true);
        WriteNuspec(Path.Combine(dir, $"{SamplePackageId}.nuspec"), SamplePackageId, tags);
    }

    private static void BuildSampleNupkg(string outPath, string tags)
    {
        using var fs = File.Create(outPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var nuspecEntry = archive.CreateEntry($"{SamplePackageId}.nuspec");
        using (var s = nuspecEntry.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write(BuildNuspecXml(SamplePackageId, tags));
        }

        var dllEntry = archive.CreateEntry($"lib/net10.0/{SampleAssemblyFile}");
        using (var s = dllEntry.Open())
        using (var src = File.OpenRead(LocateSampleAssembly()))
        {
            src.CopyTo(s);
        }
    }

    private static void WriteNuspec(string path, string id, string tags)
        => File.WriteAllText(path, BuildNuspecXml(id, tags));

    private static string BuildNuspecXml(string id, string tags) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
           <metadata>
             <id>{id}</id>
             <version>1.0.0</version>
             <description>test</description>
             <authors>test</authors>
             <tags>{tags}</tags>
           </metadata>
         </package>
         """;
}
