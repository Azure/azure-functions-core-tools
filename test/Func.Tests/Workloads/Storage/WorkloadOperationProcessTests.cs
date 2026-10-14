// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public sealed class WorkloadOperationProcessTests
{
    [Fact]
    public async Task IndependentlyPreparedB_WaitsForActualAStackAndNativeFailure_ThenRevalidatesUnderLease()
    {
        using var fixture = new BasicAcquisitionFixture();
        string manifest = Path.Combine(fixture.Root, "approved-fixture.json");
        await File.WriteAllTextAsync(manifest, fixture.Catalog.Json);
        await using var b = OwnedWorkloadProcess.Start("basic", fixture.Paths.Home, fixture.Root, manifest, "success", "Fixture.StackB");
        await b.WaitForAsync("PREPARED");
        await using var a = OwnedWorkloadProcess.Start("basic", fixture.Paths.Home, fixture.Root, manifest, "fail", "Fixture.StackA");
        await a.WaitForAsync("PREPARED");
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
        await a.SignalAsync();
        await a.WaitForAsync("TEMPLATE_MUTATING");
        var retained = await fixture.Store.GetWorkloadsAsync();
        retained.Should().ContainSingle().Which.LogicalPackage!.PackageId.Should().Be("fixture.stacka");
        await b.SignalAsync();
        await b.WaitForAsync("WAITING");
        b.Lines.Should().NotContain(line => line.StartsWith("RESULT:", StringComparison.Ordinal));

        await a.SignalAsync();
        await a.WaitForAsync("RESULT:PartialFailure:InstallFailed");
        await a.WaitForExitAsync();
        await b.WaitForAsync("RESULT:Rejected:RangeConflict");
        await b.WaitForExitAsync();

        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(retained);
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stackb.test-rid", "1.0.0")).Should().BeFalse();
        b.Lines.Where(line => line.StartsWith("PAYLOAD:", StringComparison.Ordinal)).Should().Equal("PAYLOAD:2.0.0");
        TemplateStore templates = new(new(fixture.Paths.Home), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        using var read = await templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(fixture.Paths.Home)), CancellationToken.None);
        (await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("move")]
    public async Task IndependentRegistryWriters_PreserveBothRowsUnderRealContention(string mode)
    {
        using var fixture = new BasicAcquisitionFixture();
        if (mode == "move")
        {
            foreach (string id in new[] { "first", "second" })
            {
                await fixture.Store.SaveWorkloadAsync(new()
                {
                    PackageId = id, PackageVersion = "1.0.0", Kind = WorkloadKind.Content, IsImplicitlyInstalled = true,
                    LogicalPackage = new() { PackageId = id + ".pointer", PackageVersion = "1.0.0" },
                });
            }
        }

        await using var first = OwnedWorkloadProcess.Start(mode, fixture.Paths.Home, "first", "first.pointer");
        await first.WaitForAsync("BEFORE_REGISTRY");
        await using var second = OwnedWorkloadProcess.Start(mode, fixture.Paths.Home, "second", "second.pointer");
        await second.WaitForAsync("WAITING");
        second.Lines.Should().NotContain("BEFORE_REGISTRY");

        await first.SignalAsync();
        await first.WaitForExitAsync();
        await second.WaitForAsync("BEFORE_REGISTRY");
        await second.SignalAsync();
        await second.WaitForExitAsync();

        var rows = await fixture.Store.GetWorkloadsAsync();
        rows.Select(row => row.PackageId).Should().BeEquivalentTo(["first", "second"]);
        rows.Select(row => row.PackageVersion).Should().OnlyContain(version => version == (mode == "move" ? "2.0.0" : "1.0.0"));
        if (mode == "move")
        {
            rows.Select(row => row.LogicalPackage!.PackageId).Should().BeEquivalentTo(["first.pointer", "second.pointer"]);
            rows.Select(row => row.InstallRefCount).Should().OnlyContain(count => count == 1);
        }
    }

    [Fact]
    public async Task IndependentPreparedDeployments_PreservePayloadsAndRows()
    {
        using var fixture = new BasicAcquisitionFixture();
        await using var first = OwnedWorkloadProcess.Start("install", fixture.Paths.Home, fixture.Archive("Fixture.StackA.test-rid"));
        await using var second = OwnedWorkloadProcess.Start("install", fixture.Paths.Home, fixture.Archive("Fixture.StackB.test-rid"));
        await first.WaitForAsync("PREPARED");
        await second.WaitForAsync("PREPARED");
        using (var owner = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None))
        {
            await first.SignalAsync();
            await second.SignalAsync();
            await first.WaitForAsync("WAITING");
            await second.WaitForAsync("WAITING");
        }

        await first.WaitForExitAsync();
        await second.WaitForExitAsync();
        var rows = await fixture.Store.GetWorkloadsAsync();
        rows.Select(row => row.PackageId).Should().BeEquivalentTo(["fixture.stacka.test-rid", "fixture.stackb.test-rid"]);
        foreach (var row in rows)
        {
            File.ReadAllText(Path.Combine(fixture.Paths.GetInstallDirectory(row.PackageId, row.PackageVersion), "tools", "any", "fixture.txt"))
                .Should().Be("Installation fixture, not an activated stack implementation.");
        }
    }

    [Theory]
    [InlineData("uninstall")]
    [InlineData("reuse")]
    [InlineData("update")]
    public async Task DirectDeploymentWriters_UseSameStoreCapability(string mode)
    {
        using var fixture = new BasicAcquisitionFixture();
        string archive = fixture.Archive("Fixture.StackA.test-rid");
        await fixture.Installer.InstallFromPackageAsync(archive);
        const string id = "fixture.stacka.test-rid";
        string[] arguments = mode switch
        {
            "reuse" => [mode, fixture.Paths.Home, id, "fixture.pointer"],
            "update" => [mode, fixture.Paths.Home, fixture.Archive("Fixture.StackA.test-rid", "2.0.0")],
            _ => [mode, fixture.Paths.Home, id],
        };
        byte[] before = await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath);
        await using var holder = OwnedWorkloadProcess.Start("hold", fixture.Paths.Home);
        await holder.WaitForAsync("ACQUIRED");
        await using var writer = OwnedWorkloadProcess.Start(arguments);
        if (mode == "update")
        {
            await writer.WaitForAsync("PREPARED");
            await writer.SignalAsync();
        }
        else
        {
            await writer.WaitForAsync("STARTING");
        }

        await writer.WaitForAsync("WAITING");
        (await File.ReadAllBytesAsync(fixture.Paths.WorkloadRegistryPath)).Should().Equal(before);
        await holder.SignalAsync();
        await holder.WaitForExitAsync();
        await writer.WaitForExitAsync();
        var row = (await fixture.Store.GetWorkloadsAsync()).SingleOrDefault();
        if (mode == "uninstall")
        {
            row.Should().BeNull();
            Directory.Exists(fixture.Paths.GetInstallDirectory(id, "1.0.0")).Should().BeFalse();
        }
        else
        {
            row.Should().NotBeNull();
            row!.PackageVersion.Should().Be(mode == "update" ? "2.0.0" : "1.0.0");
            row.LogicalPackage?.PackageId.Should().Be(mode == "reuse" ? "fixture.pointer" : null);
        }
    }

    [Fact]
    public async Task CanceledWaiterAndKilledCleanHolder_DoNotMutateRegistry()
    {
        using var fixture = new BasicAcquisitionFixture();
        await using var holder = OwnedWorkloadProcess.Start("hold", fixture.Paths.Home);
        await holder.WaitForAsync("ACQUIRED");
        await using var waiter = OwnedWorkloadProcess.Start("cancel-wait", fixture.Paths.Home);
        await waiter.WaitForAsync("WAITING");
        await waiter.SignalAsync();
        await waiter.WaitForAsync("CANCELED");
        await waiter.WaitForExitAsync();

        await holder.TerminateAsync();
        await using var next = OwnedWorkloadProcess.Start("hold", fixture.Paths.Home);
        await next.WaitForAsync("ACQUIRED");
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        File.Exists(Intent(fixture.Paths)).Should().BeFalse();
    }

    [Fact]
    public async Task KilledDirectoryBeforeRegistry_BlocksNextParticipantWithRecoveryRequired()
    {
        using var fixture = new BasicAcquisitionFixture();
        await using var writer = OwnedWorkloadProcess.Start("directory-crash", fixture.Paths.Home, fixture.Archive("Fixture.StackA.test-rid"));
        await writer.WaitForAsync("PREPARED");
        await writer.SignalAsync();
        await writer.WaitForAsync("BEFORE_REGISTRY");
        string payload = Path.Combine(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0"), "tools", "any", "fixture.txt");
        File.Exists(payload).Should().BeTrue();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        File.Exists(Intent(fixture.Paths)).Should().BeTrue();

        await writer.TerminateAsync();
        await using var next = OwnedWorkloadProcess.Start("hold", fixture.Paths.Home);
        await next.WaitForAsync("RECOVERY_REQUIRED");
        await next.WaitForExitAsync();

        File.Exists(payload).Should().BeTrue();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        File.Exists(Intent(fixture.Paths)).Should().BeTrue();
    }

    [Fact]
    public async Task KilledAfterVerifiedStackDuringNativeTemplateCommit_PreservesStackAndRecoversTemplateJournal()
    {
        using var fixture = new BasicAcquisitionFixture();
        await using var writer = OwnedWorkloadProcess.Start("template-crash", fixture.Paths.Home,
            fixture.Archive("Fixture.StackA.test-rid"), fixture.Archive());
        await writer.WaitForAsync("PREPARED");
        await writer.SignalAsync();
        await writer.WaitForAsync("TEMPLATE_MUTATING");
        var before = await fixture.Store.GetWorkloadsAsync();
        before.Should().ContainSingle();
        File.Exists(Intent(fixture.Paths)).Should().BeFalse();
        TemplateStore templates = new(new(fixture.Paths.Home), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        File.Exists(templates.Journal).Should().BeTrue();

        await writer.TerminateAsync();
        using (var operation = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None))
        using (var read = await templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(fixture.Paths.Home)), CancellationToken.None))
        {
            (await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        }

        File.Exists(templates.Journal).Should().BeFalse();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(before);
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0")).Should().BeTrue();
    }

    private static string Intent(IWorkloadPaths paths)
        => Path.Combine(paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName, FileWorkloadOperationCoordinator.IntentFileName);
}
