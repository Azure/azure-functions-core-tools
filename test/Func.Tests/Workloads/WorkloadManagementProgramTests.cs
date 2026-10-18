// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Workloads;
using System.Security.Cryptography;
using System.Text.Json;

namespace Azure.Functions.Cli.Tests.Workloads;

public sealed class WorkloadManagementProgramTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-management-").FullName;

    [Fact]
    public async Task Program_InstalledOtherRidMetadata_JsonRemainsOneInventoryDocument()
    {
        string home = Path.Combine(_root, "cli");
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        var store = new WorkloadStore(paths, new FileWorkloadOperationCoordinator(paths));
        await store.SaveWorkloadAsync(new()
        {
            PackageId = "fixture.other-rid",
            PackageVersion = "1.0.0",
            Kind = WorkloadKind.Content,
            RuntimeIdentifier = "other-rid",
            Source = "fixture",
        });
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
            ["workload", "list", "--all-versions", "--json"], timeout.Token);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        using var inventory = JsonDocument.Parse(result.StandardOutput);
        inventory.RootElement.EnumerateArray().Should().ContainSingle().Which.GetProperty("runtimeIdentifier").GetString().Should().Be("other-rid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_ManagementMetadata_NeverCreatesColdNativeHiveOrRecoversAnExistingJournal(bool installed)
    {
        string home = Path.Combine(_root, "cli");
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        if (installed)
        {
            string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-node-artifacts"), "*.nupkg").Single();
            var install = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
                ["workload", "install", archive], timeout.Token);
            install.ExitCode.Should().Be(0, install.StandardOutput + install.StandardError);
            File.WriteAllText(Path.Combine(home, "templates", "preserved-live.txt"), "Native recovery must not run for metadata commands.");
            File.WriteAllText(Path.Combine(home, ".template-store", "recovery.ready"), "present");
        }
        var before = SnapshotNative(home);

        var listed = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
            ["--verbose=false", "workload", "list", "--all-versions", "--json"], timeout.Token);
        var help = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
            ["workload", "--help"], timeout.Token);

        listed.ExitCode.Should().Be(0, listed.StandardOutput + listed.StandardError);
        help.ExitCode.Should().Be(0, help.StandardOutput + help.StandardError);
        using var inventory = JsonDocument.Parse(listed.StandardOutput);
        if (installed)
        {
            var entry = inventory.RootElement.EnumerateArray().Should().ContainSingle().Subject;
            entry.GetProperty("loaded").GetBoolean().Should().BeFalse("metadata registration never loads a runtime instance");
            File.ReadAllText(Path.Combine(home, ".template-store", "recovery.ready")).Should().Be("present");
        }
        else
        {
            inventory.RootElement.GetArrayLength().Should().Be(0);
            Directory.Exists(Path.Combine(home, "templates")).Should().BeFalse();
            Directory.Exists(Path.Combine(home, ".template-store")).Should().BeFalse();
        }
        var after = SnapshotNative(home);
        after.Keys.Should().BeEquivalentTo(before.Keys);
        foreach (var file in before)
        {
            after[file.Key].Should().Be(file.Value, "metadata commands must preserve {0}", file.Key);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Program_UninstallActiveNormalBundledWorkload_DoesNotLoadRuntimeAndRemovesExactPayload(bool corruptRuntime)
    {
        string home = Path.Combine(_root, "cli");
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        var store = new WorkloadStore(paths, new FileWorkloadOperationCoordinator(paths));
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-node-artifacts"), "*.nupkg").Single();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        var installed = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
            ["workload", "install", archive], timeout.Token);
        installed.ExitCode.Should().Be(0, installed.StandardOutput + installed.StandardError);
        var entry = (await store.GetWorkloadsAsync(timeout.Token)).Should().ContainSingle().Subject;
        string directory = paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        if (corruptRuntime)
        {
            File.WriteAllText(Path.Combine(directory, entry.EntryPoint!.AssemblyPath), "Not a loadable assembly.");
        }

        var removed = await SetupProgramAdvisoryTests.RunCliAsync(_root, home, paths.Home, _root,
            ["workload", "uninstall", entry.PackageId, "--exact"], timeout.Token);

        removed.ExitCode.Should().Be(0, removed.StandardOutput + removed.StandardError);
        removed.StandardOutput.Should().Contain("Uninstalled workload");
        removed.StandardOutput.Should().NotContain("could not be loaded").And.NotContain("failed to initialize");
        removed.StandardError.Should().NotContain("could not be loaded").And.NotContain("failed to initialize");
        Directory.Exists(directory).Should().BeFalse();
        (await store.GetWorkloadsAsync(timeout.Token)).Should().BeEmpty();
        File.Exists(Path.Combine(paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        string receipts = Path.Combine(home, "templates", ".bundled-owners");
        Directory.GetFiles(receipts, "*.json", SearchOption.AllDirectories).Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Dictionary<string, string> SnapshotNative(string home)
    {
        Dictionary<string, string> files = [];
        foreach (string directory in new[] { Path.Combine(home, "templates"), Path.Combine(home, ".template-store") })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                files.Add(Path.GetRelativePath(home, path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            }
        }
        return files;
    }
}
