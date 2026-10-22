// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_UnbundledStack_RequiresExplicitCompatibleUpdateBeforeGenerationIncludingForce(bool force)
    {
        string archive = Path.Combine(_root, "unbundled-node.nupkg");
        File.Copy(BuiltArchive, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry("bundled-templates.json")!.Delete();
        }
        var installed = await _installer.InstallFromPackageAsync(archive);
        string project = Path.Combine(_root, "old-install-target");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "user.txt"), "preserve before compatible stack exists");
        var before = PublicSnapshot(project);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        string processHome = Path.Combine(_root, "old-install-process");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        string[] flags = force ? ["--force"] : [];

        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", "js", "--skip-npm-install", .. flags], timeout.Token);

        initialized.ExitCode.Should().Be(1);
        string.Join(' ', initialized.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("func workload update " + installed.Entry.PackageId).And.Contain("--source");
        PublicSnapshot(project).Should().BeEquivalentTo(before);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        _source.ReceivedCalls().Should().BeEmpty();

        // Configuration-only adoption remains available even before a template-capable update.
        File.WriteAllText(Path.Combine(project, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(project, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        File.WriteAllText(Path.Combine(project, "package.json"), """{"main":"src/functions/*.js","dependencies":{"@azure/functions":"^4.0.0"}}""");
        var adopted = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", "js"], timeout.Token);
        adopted.ExitCode.Should().Be(0, adopted.StandardError);
        adopted.StandardOutput.Should().Contain("Existing project adopted");
        string configurationPath = Path.Combine(project, ".func", "config.json");
        var configured = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configurationPath))!;
        configured["profiles"] = new System.Text.Json.Nodes.JsonArray("must-not-fetch-before-update");
        File.WriteAllText(configurationPath, configured.ToJsonString());
        var adoptedBytes = PublicSnapshot(project);
        var created = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["new", project, "-t", "http", "--output=json", "--non-interactive"], timeout.Token);
        created.ExitCode.Should().Be(1);
        created.StandardError.Should().BeEmpty();
        using var json = JsonDocument.Parse(created.StandardOutput);
        json.RootElement.GetProperty("kind").GetString().Should().Be("stackUpdateRequired");
        json.RootElement.GetProperty("diagnostics")[0].GetString().Should().Contain("func workload update " + installed.Entry.PackageId);
        PublicSnapshot(project).Should().BeEquivalentTo(adoptedBytes);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Fact]
    public async Task Program_RetainedBundleExplicitRepair_ReopensCapturedRuntimeAndCreatesWithoutAcquisition()
    {
        _inspector.FailLive = true;
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await install.Should().ThrowAsync<BundledTemplateRegistrationException>();
        var retained = (await _workloads.GetWorkloadsAsync()).Single();
        string project = Path.Combine(_root, "retained-project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "user.txt"), "force must not delete before registration is available");
        var before = PublicSnapshot(project);
        string processHome = Path.Combine(_root, "repair-process");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        string[] args = ["init", project, "-s", "node", "-l", "js", "--force", "--skip-npm-install", "--no-bundles"];
        var unavailable = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project, args, timeout.Token);
        unavailable.ExitCode.Should().Be(1);
        string.Join(' ', unavailable.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("explicitly repair retained bundled-template registration");
        PublicSnapshot(project).Should().BeEquivalentTo(before);

        _inspector.FailLive = false;
        var repaired = await _installer.EnsureInstalledAsync(retained, timeout.Token);
        repaired.AlreadyInstalled.Should().BeTrue();
        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project, args, timeout.Token);
        initialized.ExitCode.Should().Be(0, initialized.StandardError);
        initialized.StandardOutput.Should().Contain("Azure.Functions.Node.Basic.JavaScript");
        var created = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["new", project, "-t", "http", "-n", "AfterRepair", "--output=json"], timeout.Token);
        created.ExitCode.Should().Be(0, created.StandardError);
        using var json = JsonDocument.Parse(created.StandardOutput);
        json.RootElement.GetProperty("kind").GetString().Should().Be("created");
        File.ReadAllText(Path.Combine(project, "src", "functions", "AfterRepair.js")).Should().Contain("app.http('AfterRepair'");
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_ExplicitSameHomeUpdate_ReopensNativeInitAndNewWhilePreservingProjectsAndUserTemplates(bool retainOtherOwners)
    {
        var legacy = await _installer.InstallFromPackageAsync(MigrationArchive("0.1.0", bundled: false));
        Dictionary<string, string>? retainedPayload = null;
        Dictionary<string, string>? siblingPayload = null;
        string legacyPath = _paths.GetInstallDirectory(legacy.Entry.PackageId, legacy.Entry.PackageVersion);
        string siblingPath = _paths.GetInstallDirectory(legacy.Entry.PackageId, "0.0.1");
        if (retainOtherOwners)
        {
            await _workloads.AddOwnershipAsync(MigrationOwnershipEntry(legacy.Entry,
                new LogicalPackage { PackageId = "retained.pointer", PackageVersion = "0.1.0", Source = "original-pointer-source" }),
                WorkloadOwnershipKind.Logical);
            await _installer.InstallFromPackageAsync(MigrationArchive("0.0.1", bundled: false));
            retainedPayload = MigrationFiles(legacyPath);
            siblingPayload = MigrationFiles(siblingPath);
        }
        string existing = Path.Combine(_root, "user-project");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "host.json"), """{"version":"2.0","user":"keep"}""");
        File.WriteAllText(Path.Combine(existing, "source.js"), "user-owned source");
        var originalProject = PublicSnapshot(existing);
        string userPackage = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(userPackage);
        string incoming = BuiltArchive;
        await using PublicWorkloadFeed feed = new(incoming);
        string project = Path.Combine(_root, "updated-project");
        Directory.CreateDirectory(project);
        string processHome = Path.Combine(_root, "migration-process");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        var updated = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["workload", "update", legacy.Entry.PackageId, "--exact", "--source", feed.Index, "--major", "--prerelease"], timeout.Token);

        updated.ExitCode.Should().Be(0, updated.StandardOutput + updated.StandardError);
        updated.StandardOutput.Should().NotContain("not supported");
        var retained = (await _workloads.GetWorkloadsAsync()).Single(entry => entry.BundledTemplatesDeclared);
        retained.BundledTemplatesDeclared.Should().BeTrue();
        using (var archive = new NuGet.Packaging.PackageArchiveReader(incoming))
        {
            retained.PackageVersion.Should().Be(archive.NuspecReader.GetVersion().ToNormalizedString());
        }
        await AssertMigrationInstalledAsync(retained, retained.PackageVersion, userPackage, incoming, feed.Index);
        PublicSnapshot(existing).Should().BeEquivalentTo(originalProject);
        if (retainOtherOwners)
        {
            var old = (await _workloads.GetWorkloadsAsync()).Single(entry => entry.PackageVersion == legacy.Entry.PackageVersion);
            old.IsImplicitlyInstalled.Should().BeTrue();
            old.LogicalPackage!.PackageId.Should().Be("retained.pointer");
            MigrationFiles(legacyPath).Should().BeEquivalentTo(retainedPayload);
            MigrationFiles(siblingPath).Should().BeEquivalentTo(siblingPayload);
        }
        else
        {
            (await _workloads.GetWorkloadsAsync()).Should().NotContain(entry => entry.PackageVersion == legacy.Entry.PackageVersion);
        }
        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", "js", "--skip-npm-install", "--no-bundles"], timeout.Token);
        initialized.ExitCode.Should().Be(0, initialized.StandardOutput + initialized.StandardError);
        initialized.StandardOutput.Should().Contain("Azure.Functions.Node.Basic.JavaScript");
        var created = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["new", project, "-t", "HttpTrigger-JavaScript", "-n", "AfterUpdate", "--output=json"], timeout.Token);
        created.ExitCode.Should().Be(0, created.StandardOutput + created.StandardError);
        created.StandardError.Should().BeEmpty();
        using var document = JsonDocument.Parse(created.StandardOutput);
        document.RootElement.GetProperty("kind").GetString().Should().Be("created");
        File.ReadAllText(Path.Combine(project, "src", "functions", "AfterUpdate.js")).Should().Contain("app.http('AfterUpdate'");
        PublicSnapshot(existing).Should().BeEquivalentTo(originalProject);
        await _installer.EnsureInstalledAsync(retained, timeout.Token, checkOnly: true);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Program_ExplicitUpdate_UserOwnedNativePackageCollision_PreservesBothStoresAndCannotBypassGenerationGate()
    {
        var legacy = await _installer.InstallFromPackageAsync(MigrationArchive("0.1.0", bundled: false));
        await MigrationInstallUserPackageAsync(MigrationInnerArchive(BuiltArchive));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string project = Path.Combine(_root, "collision-project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "user.txt"), "user project must survive a refused explicit update");
        var original = PublicSnapshot(project);
        string processHome = Path.Combine(_root, "collision-process");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        await using PublicWorkloadFeed feed = new(BuiltArchive);

        var updated = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["workload", "update", legacy.Entry.PackageId, "--exact", "--source", feed.Index, "--major", "--prerelease"], timeout.Token);

        updated.ExitCode.Should().Be(1);
        updated.StandardError.Should().Contain("ownership");
        updated.StandardOutput.Should().NotContain("Updated");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        var after = MigrationFiles(Path.Combine(Home, "templates"));
        after.Keys.Should().BeEquivalentTo(native.Keys);
        foreach (var file in native) after[file.Key].Should().Be(file.Value, "user-owned native file {0} must be unchanged", file.Key);
        PublicSnapshot(project).Should().BeEquivalentTo(original);
        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", "js", "--force", "--skip-npm-install"], timeout.Token);
        initialized.ExitCode.Should().Be(1);
        string.Join(' ', initialized.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("func workload update " + legacy.Entry.PackageId);
        PublicSnapshot(project).Should().BeEquivalentTo(original);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }
}
