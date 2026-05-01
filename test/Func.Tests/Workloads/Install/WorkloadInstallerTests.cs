// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerTests : IDisposable
{
    private const string ValidNuspec = """
        <?xml version="1.0"?>
        <package>
          <metadata>
            <id>Test.Workload</id>
            <version>1.0.0</version>
            <title>Test Workload</title>
            <description>For tests.</description>
            <tags>test</tags>
            <packageTypes>
              <packageType name="FuncCliWorkload" />
            </packageTypes>
          </metadata>
        </package>
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("workload-installer-").FullName;
    private readonly INuspecReader _nuspec = new NuspecReader();
    private readonly IWorkloadEntryPointScanner _scanner = Substitute.For<IWorkloadEntryPointScanner>();
    private readonly IGlobalManifestStore _store = Substitute.For<IGlobalManifestStore>();
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerTests()
    {
        _paths = new WorkloadPathsOptions { Home = Path.Combine(_root, ".azure-functions") };
        _scanner.Scan(Arg.Any<string>())
            .Returns(new EntryPointSpec { Assembly = "Test.dll", Type = "Test.Type" });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task InstallFromDirectory_HappyPath_MovesAndPersists()
    {
        var staging = CreateStagingPackage(ValidNuspec);

        var installer = NewInstaller();
        var result = await installer.InstallFromDirectoryAsync(staging);

        Assert.Equal("Test.Workload", result.PackageId);
        Assert.Equal("1.0.0", result.Version);

        var installDir = _paths.GetInstallDirectory("Test.Workload", "1.0.0");
        Assert.True(Directory.Exists(installDir));
        Assert.False(Directory.Exists(staging), "staging dir should have been moved.");

        await _store.Received(1).SaveWorkloadAsync(
            "Test.Workload",
            "1.0.0",
            Arg.Is<GlobalManifestEntry>(e =>
                e.DisplayName == "Test Workload" &&
                e.InstallPath == installDir &&
                e.EntryPoint.Assembly == "Test.dll"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromDirectory_MissingPackageType_Throws_NoMove()
    {
        var staging = CreateStagingPackage("""
            <?xml version="1.0"?>
            <package><metadata>
              <id>Test.Workload</id>
              <version>1.0.0</version>
            </metadata></package>
            """);

        var installer = NewInstaller();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => installer.InstallFromDirectoryAsync(staging));

        Assert.True(ex.IsUserError);
        Assert.Contains("FuncCliWorkload", ex.Message);
        Assert.True(Directory.Exists(staging), "staging must remain on validation failure.");
        await _store.DidNotReceive().SaveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GlobalManifestEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromDirectory_MissingNuspec_Throws()
    {
        var staging = Path.Combine(_root, "no-nuspec");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "Test.dll"), "stub");

        var installer = NewInstaller();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => installer.InstallFromDirectoryAsync(staging));
        Assert.Contains(".nuspec", ex.Message);
    }

    [Fact]
    public async Task InstallFromDirectory_AlreadyInstalled_Throws()
    {
        var installDir = _paths.GetInstallDirectory("Test.Workload", "1.0.0");
        Directory.CreateDirectory(installDir);

        var staging = CreateStagingPackage(ValidNuspec);

        var installer = NewInstaller();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => installer.InstallFromDirectoryAsync(staging));
        Assert.Contains("already installed", ex.Message);
    }

    [Fact]
    public async Task InstallFromDirectory_ScannerFailure_DoesNotMove()
    {
        var staging = CreateStagingPackage(ValidNuspec);
        _scanner.Scan(Arg.Any<string>())
            .Returns(_ => throw new GracefulException("no entry point", isUserError: true));

        var installer = NewInstaller();
        await Assert.ThrowsAsync<GracefulException>(
            () => installer.InstallFromDirectoryAsync(staging));

        Assert.True(Directory.Exists(staging), "scan must run before move.");
        await _store.DidNotReceive().SaveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GlobalManifestEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromDirectory_ManifestWriteFails_RollsBackInstallDir()
    {
        var staging = CreateStagingPackage(ValidNuspec);
        _store.SaveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GlobalManifestEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("disk full"));

        var installer = NewInstaller();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallFromDirectoryAsync(staging));

        var installDir = _paths.GetInstallDirectory("Test.Workload", "1.0.0");
        Assert.False(Directory.Exists(installDir), "install dir must be rolled back when manifest write fails.");
    }

    [Fact]
    public async Task Uninstall_RemovesEntryAndDirectory()
    {
        var installDir = _paths.GetInstallDirectory("Test.Workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "Test.dll"), "stub");
        _store.RemoveWorkloadAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(true);

        var installer = NewInstaller();
        var removed = await installer.UninstallAsync("Test.Workload", "1.0.0");

        Assert.True(removed);
        Assert.False(Directory.Exists(installDir));
    }

    [Fact]
    public async Task Uninstall_NoSuchEntry_ReturnsFalse_LeavesDirectoryAlone()
    {
        // Set up a leftover directory that does NOT match a manifest entry.
        var installDir = _paths.GetInstallDirectory("Test.Workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        _store.RemoveWorkloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var installer = NewInstaller();
        var removed = await installer.UninstallAsync("Test.Workload", "1.0.0");

        Assert.False(removed);
        Assert.True(Directory.Exists(installDir), "uninstall must not touch the dir when the manifest had no entry.");
    }

    private WorkloadInstaller NewInstaller() => new(_paths, _store, _nuspec, _scanner);

    private string CreateStagingPackage(string nuspecContent)
    {
        var staging = Path.Combine(_root, $"staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "Test.Workload.nuspec"), nuspecContent);
        File.WriteAllText(Path.Combine(staging, "Test.dll"), "stub");
        return staging;
    }
}
