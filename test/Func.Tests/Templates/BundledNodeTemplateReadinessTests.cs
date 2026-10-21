// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("ensure")]
    [InlineData("setup")]
    [InlineData("update")]
    public async Task ReadOnlyReadiness_PendingNativeRecovery_RefusesWithoutChangingRetainedState(string path)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        SeedPendingNativeRecovery();
        var before = NativeState();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Exception? error;
        SetupDependencyResult? result = null;

        if (path == "setup")
        {
            var setup = new SetupDependencyInstaller(_interaction, _workloads, Substitute.For<IWorkloadCatalog>(), _installer);
            var options = new SetupCommandOptions(new(_root), ["node"], [], null, SetupInstallPolicy.IfNeeded,
                true, true, true, true, SetupOutputMode.Json);
            error = await Record.ExceptionAsync(async () =>
                result = await setup.EnsureDependencyAsync(options, SetupDependency.Stack("node"), CancellationToken.None));
        }
        else if (path == "update")
        {
            var entry = installed.Entry;
            _source.ResolveLatestVersionAsync(entry.PackageId, true, Arg.Any<NuGetVersion>(), false, entry.Source, Arg.Any<CancellationToken>())
                .Returns(new ResolvedPackage(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), new(entry.Source)));
            error = await Record.ExceptionAsync(async () =>
                await _installer.UpdateAsync(entry.PackageId, null, entry.Source, true, false));
        }
        else
        {
            error = await Record.ExceptionAsync(async () =>
                await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true));
        }

        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        if (path == "setup")
        {
            error.Should().BeNull();
            result!.Status.Should().Be(SetupDependencyStatus.Failed);
            result.Message.Should().Contain("requires recovery");
        }
        else
        {
            error.Should().BeOfType<InvalidWorkloadException>().Which.Message.Should().Contain("requires recovery");
        }
    }

    [Theory]
    [InlineData("present")]
    [InlineData("damaged")]
    public async Task ValidateInstalled_RecoveryAppearsAfterPreparation_RefusesWithoutChangingRetainedState(string journal)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        string directory = _paths.GetInstallDirectory(installed.Entry.PackageId, installed.Entry.PackageVersion);
        var origin = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, BundledTemplateInstaller.OriginName)),
            BundledTemplateJsonContext.Default.BundledTemplateOrigin)!;
        var bundles = new BundledTemplateInstaller(_templates, _inspector, _files, new(Home));
        using var prepared = await bundles.PrepareAsync(directory, origin, CancellationToken.None, checkOnly: true);
        SeedPendingNativeRecovery(journal);
        var before = NativeState();

        var error = await Record.ExceptionAsync(() => bundles.ValidateInstalledAsync(prepared!, CancellationToken.None));

        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        error.Should().BeOfType<InvalidWorkloadException>().Which.Message.Should().Contain("requires recovery");
    }

    [Fact]
    public async Task EnsureInstalled_NormalRepair_PreservesOwningStoreRecovery()
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        SeedPendingNativeRecovery();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None);

        result.AlreadyInstalled.Should().BeTrue();
        File.Exists(Path.Combine(Home, "templates", "after-snapshot.txt")).Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    private void SeedPendingNativeRecovery(string journal = "present")
    {
        string control = Path.Combine(Home, ".template-store");
        string backup = Path.Combine(control, "recovery");
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, recursive: true);
        }
        _files.CopyDirectory(Path.Combine(Home, "templates"), backup);
        File.WriteAllText(Path.Combine(control, "recovery.ready"), journal);
        File.WriteAllText(Path.Combine(Home, "templates", "after-snapshot.txt"), "Retained pending state must not be recovered by a check.");
    }

    private Dictionary<string, string> NativeState()
        => Directory.GetFiles(Home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Home, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
