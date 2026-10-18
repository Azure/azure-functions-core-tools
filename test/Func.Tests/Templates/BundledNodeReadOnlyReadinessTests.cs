// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadinessCheck_PendingNativeJournal_RefusesWithoutRecoveringOrChangingEitherStore(bool corrupt)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        SeedReadinessJournal(corrupt);
        var native = ReadinessFiles();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> check = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);

        await check.Should().ThrowAsync<TemplateStoreRecoveryRequiredException>().WithMessage("*requires recovery*explicit*");

        AssertReadinessFiles(native);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadinessCheck_JournalAppearsAfterPreparation_ValidationRetainsTheReadOnlyAdmissionPolicy(bool corrupt)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        using var prepared = await deployment.PrepareReuseAsync(installed.Entry, CancellationToken.None, requireCleanStore: true);
        prepared.BundledTemplates!.RequireCleanStore.Should().BeTrue();
        SeedReadinessJournal(corrupt);
        var native = ReadinessFiles();
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> validate = () => deployment.ValidateReuseAsync(operation, prepared, CancellationToken.None, checkOnly: true);

        await validate.Should().ThrowAsync<TemplateStoreRecoveryRequiredException>().WithMessage("*requires recovery*explicit*");

        AssertReadinessFiles(native);
        operation.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task ReadinessCheck_CleanStore_VerifiesWithoutChangingNativeState()
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        var native = ReadinessFiles();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);

        result.AlreadyInstalled.Should().BeTrue();
        AssertReadinessFiles(native);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitReadinessRepair_PendingNativeJournal_RetainsNormalRecoveryAndCorruptJournalFailure(bool corrupt)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        SeedReadinessJournal(corrupt);
        var native = ReadinessFiles();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> repair = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None);

        if (corrupt)
        {
            await repair.Should().ThrowAsync<InvalidDataException>().WithMessage("*recovery snapshot is incomplete*");
            AssertReadinessFiles(native);
        }
        else
        {
            await repair();
            File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
            File.Exists(Path.Combine(Home, "templates", "live-only.txt")).Should().BeFalse();
            await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);
        }
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    private void SeedReadinessJournal(bool corrupt)
    {
        if (Directory.Exists(_templates.Backup)) Directory.Delete(_templates.Backup, recursive: true);
        _files.CopyDirectory(Path.Combine(Home, "templates"), _templates.Backup);
        File.WriteAllText(Path.Combine(Home, "templates", "live-only.txt"), "Read-only readiness must preserve this live file.");
        File.WriteAllText(_templates.Journal, corrupt ? "corrupt" : "present");
    }

    private Dictionary<string, string> ReadinessFiles()
    {
        var files = MigrationFiles(Path.Combine(Home, "templates")).ToDictionary(file => "templates\\" + file.Key, file => file.Value);
        foreach (var file in MigrationFiles(Path.Combine(Home, ".template-store")))
        {
            files.Add(".template-store\\" + file.Key, file.Value);
        }
        return files;
    }

    private void AssertReadinessFiles(IReadOnlyDictionary<string, string> expected)
    {
        var actual = ReadinessFiles();
        actual.Keys.Should().BeEquivalentTo(expected.Keys);
        foreach (var file in expected)
        {
            actual[file.Key].Should().Be(file.Value, "readiness must preserve {0}", file.Key);
        }
    }
}
