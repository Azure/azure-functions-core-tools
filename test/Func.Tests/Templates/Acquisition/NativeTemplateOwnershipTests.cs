// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed class NativeTemplateOwnershipTests
{
    [Fact]
    public async Task EnsureAsync_QualifiedNativeArchive_RecordsProvenanceAndRepeatDoesNotRewriteOrAcquire()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var artifact = NativeArtifact(fixture);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(fixture.Root)) { RequireCleanStore = true };

        (await lifecycle.EnsureAsync(artifact, context, false, CancellationToken.None)).Should().BeFalse();
        var before = fixture.HiveHashes();
        var provenance = await owner.GetAsync(artifact.PackageId, "1.0.0", artifact.Sha256, CancellationToken.None);
        (await lifecycle.EnsureAsync(artifact, context, false, CancellationToken.None)).Should().BeTrue();

        provenance.Should().Be(new BasicTemplateOwner(BasicAcquisitionFixture.Source, "1.0.0"));
        fixture.Payload.Opened.Should().Equal("1.0.0");
        fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        (await fixture.ReadTemplatesAsync()).Should().ContainSingle().Which.Identity.Should().Be("Fixture.Basic.Item");
    }

    [Fact]
    public async Task EnsureAsync_ProvenanceWriteFails_RollsBackActualNativeRegistration()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var receiptFiles = Substitute.For<IFileSystem>();
        receiptFiles.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => files.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        receiptFiles.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new IOException("Owned native provenance failure.")));
        var owner = new NativeTemplateOwnership(receiptFiles, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var before = fixture.HiveHashes();

        Func<Task> install = () => lifecycle.EnsureAsync(NativeArtifact(fixture),
            new(WorkingDirectory.FromExplicit(fixture.Root)), false, CancellationToken.None);

        await install.Should().ThrowAsync<IOException>().WithMessage("*provenance failure*");
        fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
        File.Exists(fixture.Templates.Journal).Should().BeFalse();
    }

    [Fact]
    public async Task SelectAsync_MissingPackageWithRetainedPin_UsesQualifiedOlderBytesWithoutClaimingReadiness()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var older = NativeArtifact(fixture);
        var latest = NativeArtifact(fixture, "2.0.0");
        await owner.RecordAsync(older, CancellationToken.None);
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(fixture.Root));

        var selected = await lifecycle.SelectAsync(new(older.PackageId, VersionRange.Parse("[1.0.0,3.0.0)"), [latest, older]),
            context, CancellationToken.None);
        using var prepared = await lifecycle.PrepareAsync(selected, context, false, CancellationToken.None);
        (await lifecycle.CommitAsync(prepared, context, CancellationToken.None)).Should().BeFalse();

        selected.Installed.Should().BeFalse();
        selected.Artifact.Should().BeSameAs(older);
        fixture.Payload.Opened.Should().Equal("1.0.0");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("digest")]
    public async Task SelectAsync_RetainedConflictingProvenance_RefusesBeforePayloadAcquisition(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var artifact = NativeArtifact(fixture);
        await owner.RecordAsync(artifact, CancellationToken.None);
        var candidate = change switch
        {
            "version" => NativeArtifact(fixture, "2.0.0"),
            "source" => artifact with { Source = "https://different.invalid/v3/index.json" },
            _ => artifact with { Sha256 = new string('a', 64) },
        };
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var before = fixture.HiveHashes();

        Func<Task> select = () => lifecycle.SelectAsync(new(artifact.PackageId, VersionRange.Parse("[1.0.0,3.0.0)"), [candidate]),
            new(WorkingDirectory.FromExplicit(fixture.Root)), CancellationToken.None);

        await select.Should().ThrowAsync<BasicAcquisitionException>().WithMessage("*No automatic replacement*");
        fixture.Payload.Opened.Should().BeEmpty();
        fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnsureAsync_IdenticalUserInstalledArchiveWithoutNativeReceipt_IsNotAdopted()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(fixture.Root));
        var unowned = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), fixture.Ownership, files);
        await unowned.EnsureAsync(fixture.Artifact(), context, false, CancellationToken.None);
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var before = fixture.HiveHashes();

        Func<Task> install = () => lifecycle.EnsureAsync(NativeArtifact(fixture), context, false, CancellationToken.None);

        (await install.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.OwnershipUnavailable);
        fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("duplicate")]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("pin")]
    public async Task GetRetainedAsync_MalformedOrConflictingReceipt_IsNotDiscardedOrTreatedAsApproval(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var artifact = NativeArtifact(fixture);
        await owner.RecordAsync(artifact, CancellationToken.None);
        string path = Path.Combine(fixture.Templates.Hive, ".native-template-owners", "fixture.basic.json");
        string json = File.ReadAllText(path);
        var document = JsonNode.Parse(json)!;
        switch (change)
        {
            case "identity": document["packageId"] = "Other.Identity"; break;
            case "version": document["version"] = "invalid"; break;
            case "source": document["source"] = "not-a-feed"; break;
            case "digest": document["sha256"] = "invalid"; break;
            case "pin": document["pinnedVersion"] = "2.0.0"; break;
        }
        string damaged = change switch
        {
            "json" => "{invalid",
            "duplicate" => json.Replace("\"version\":\"1.0.0\"", "\"version\":\"1.0.0\",\"Version\":\"2.0.0\"", StringComparison.Ordinal),
            _ => document.ToJsonString(),
        };
        File.WriteAllText(path, damaged);

        Func<Task> read = () => owner.GetRetainedAsync(artifact.PackageId, CancellationToken.None);

        (await read.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        File.ReadAllText(path).Should().Be(damaged);
    }

    [Fact]
    public async Task NativeOwnership_NullDependenciesAndInvalidInputs_RejectProgrammerErrors()
    {
        PhysicalFileSystem files = new();
        Action missingFiles = () => new NativeTemplateOwnership(null!, new("unused"));
        Action missingPaths = () => new NativeTemplateOwnership(files, null!);
        missingFiles.Should().Throw<ArgumentNullException>();
        missingPaths.Should().Throw<ArgumentNullException>();
        var owner = new NativeTemplateOwnership(files, new("unused"));
        Func<Task>[] invalid =
        [
            () => owner.RecordAsync(null!, CancellationToken.None),
            () => owner.GetRetainedAsync("", CancellationToken.None),
            () => owner.GetAsync("Valid.Id", "", new string('a', 64), CancellationToken.None),
            () => owner.GetAsync("Valid.Id", "1.0.0", "invalid", CancellationToken.None),
            () => owner.GetRetainedAsync("../unsafe", CancellationToken.None),
        ];
        foreach (var call in invalid)
        {
            await call.Should().ThrowAsync<ArgumentException>();
        }
    }

    [Fact]
    public async Task PrepareAsync_AdditionalProjectRole_CannotRelabelAnActualItemAsAProject()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var artifact = NativeArtifact(fixture) with
        {
            Templates = [new("basic/.template.config/template.json", "Fixture.Basic.Item", "project")],
            ContentRoles = [new("Fixture.Basic.Item", NativeTemplateContentRole.AdditionalProject)],
        };

        Func<Task> install = () => lifecycle.EnsureAsync(artifact, new(WorkingDirectory.FromExplicit(fixture.Root)), false, CancellationToken.None);

        (await install.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidArtifact);
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_QualifiedBytesWithMalformedNativeHostMetadata_RefusesBeforeLiveRegistration()
    {
        using var fixture = new BasicAcquisitionFixture();
        using (var zip = ZipFile.Open(fixture.Archive(), ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("basic/.template.config/func.host.json").Open()))
        {
            writer.Write("""{"symbolInfo":42}""");
        }
        var artifact = NativeArtifact(fixture) with
        {
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.Archive()))),
        };
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);

        Func<Task> install = () => lifecycle.EnsureAsync(artifact, new(WorkingDirectory.FromExplicit(fixture.Root)), false, CancellationToken.None);

        (await install.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidArtifact);
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_TemplateIdentityBecomesOwnedAfterPreparation_RefusesAndPreservesActualOtherPackage()
    {
        using var fixture = new BasicAcquisitionFixture();
        PhysicalFileSystem files = new();
        var owner = new NativeTemplateOwnership(files, new(Path.Combine(fixture.Root, "cli-home")));
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, new BasicArtifactInspector(files), owner, files);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(fixture.Root));
        var artifact = NativeArtifact(fixture);
        using var prepared = await lifecycle.PrepareAsync(new(artifact, false), context, false, CancellationToken.None);
        fixture.UseIndependentBCompanion();
        var other = new BasicCompanionManifestReader().Read(fixture.Catalog.Json).Artifacts.Single(item => item.PackageId == "Fixture.Unrelated");
        string archive = fixture.Archive(other.PackageId);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("unrelated/.template.config/template.json")!;
            JsonNode configuration;
            using (var input = entry.Open())
            {
                configuration = JsonNode.Parse(input)!;
            }
            configuration["identity"] = "Fixture.Basic.Item";
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("unrelated/.template.config/template.json").Open());
            writer.Write(configuration.ToJsonString());
        }
        other = other with
        {
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))),
            Templates = [new("unrelated/.template.config/template.json", "Fixture.Basic.Item", "item")],
            ContentRoles = [new("Fixture.Basic.Item", NativeTemplateContentRole.Function)],
        };
        await lifecycle.EnsureAsync(other, context, false, CancellationToken.None);
        var before = fixture.HiveHashes();

        Func<Task> commit = () => lifecycle.CommitAsync(prepared, context, CancellationToken.None);

        await commit.Should().ThrowAsync<BasicAcquisitionException>().WithMessage("*Another native package owns*");
        fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        using var reader = await fixture.Templates.OpenReadAsync(context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Should().ContainSingle().Which.Identifier.Should().Be("Fixture.Unrelated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LegacyPrototypeRecovery_CheckRefusesButOrdinaryRepairStillRecovers(bool check)
    {
        using var fixture = new BasicAcquisitionFixture();
        (await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        PhysicalFileSystem files = new();
        if (Directory.Exists(fixture.Templates.Backup))
        {
            Directory.Delete(fixture.Templates.Backup, recursive: true);
        }
        files.CopyDirectory(fixture.Templates.Hive, fixture.Templates.Backup);
        File.WriteAllText(fixture.Templates.Journal, "present");
        string marker = Path.Combine(fixture.Templates.Hive, "after-snapshot.txt");
        File.WriteAllText(marker, "A read-only check must preserve pending native state.");
        var before = fixture.HiveHashes();

        var result = await fixture.Runner().RunAsync(fixture.Request(check: check), CancellationToken.None);

        if (check)
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
            result.Failure.Should().Be(BasicAcquisitionFailure.RecoveryRequired);
            fixture.HiveHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
                .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            File.ReadAllText(fixture.Templates.Journal).Should().Be("present");
        }
        else
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Completed);
            File.Exists(marker).Should().BeFalse();
            File.Exists(fixture.Templates.Journal).Should().BeFalse();
        }
    }

    [Fact]
    public async Task OwnedUpdate_InstallsExactNewArchiveAndTransitionsReceiptThenRetriesReadOnly()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        BasicOwnedTemplateUpdate update = scenario.Update;
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(update.After, false) { Update = update }, scenario.Context, false, CancellationToken.None);
        Dictionary<string, string> beforeUpdate = scenario.Fixture.HiveHashes();

        bool reused = await scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        reused.Should().BeFalse();
        using (TemplateEngineSession reader = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, CancellationToken.None))
        {
            (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
                .Should().ContainSingle().Which.Version.Should().Be("1.0.1");
        }
        Directory.GetFiles(Path.Combine(scenario.Fixture.Templates.Hive, "packages"), "*.nupkg")
            .Select(Path.GetFileName).Should().ContainSingle().Which.Should().Contain("1.0.1");
        NativeTemplateOwnerProof receipt = (await scenario.Owner.GetRetainedProofAsync(update.After.PackageId, CancellationToken.None))!;
        receipt.Receipt.Version.Should().Be(update.After.Version.ToNormalizedString());
        receipt.Receipt.ContentRevision.Should().Be(update.After.ContentRevision);
        receipt.Receipt.PinnedVersion.Should().Be(update.After.Version.ToNormalizedString());
        receipt.RawSha256.Should().Be(scenario.Owner.CreateProof(update.After).RawSha256);
        scenario.Fixture.HiveHashes().Should().NotBeEquivalentTo(beforeUpdate);

        BasicTemplateSelection retry = await scenario.Lifecycle.RevalidateOwnedUpdateAsync(scenario.Plan, update,
            scenario.Context, CancellationToken.None);
        retry.Installed.Should().BeTrue();
        int openedPayloads = scenario.Payload.Opened.Count;
        using PreparedBasicTemplate noOp = await scenario.Lifecycle.PrepareAsync(retry, scenario.Context, false, CancellationToken.None);
        Dictionary<string, string> beforeRetry = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeRetryBackup = scenario.BackupHashes();

        bool alreadyCompleted = await scenario.Lifecycle.CommitAsync(noOp, scenario.Context, CancellationToken.None);

        alreadyCompleted.Should().BeTrue();
        scenario.Payload.Opened.Should().HaveCount(openedPayloads);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeRetry);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeRetryBackup);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
    }

    [Theory]
    [InlineData("raw-bytes")]
    [InlineData("content-revision")]
    public async Task OwnedUpdate_AfterReceiptNearMatch_RefusesReadOnlyRetry(string change)
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        await scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);
        if (change == "raw-bytes")
        {
            File.AppendAllText(scenario.ReceiptPath, " ");
        }
        else
        {
            JsonNode receipt = JsonNode.Parse(File.ReadAllText(scenario.ReceiptPath))!;
            receipt["contentRevision"] = "different-after-revision";
            File.WriteAllText(scenario.ReceiptPath, receipt.ToJsonString());
        }
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeBackup = scenario.BackupHashes();
        int openedPayloads = scenario.Payload.Opened.Count;

        Func<Task> retry = () => scenario.Lifecycle.RevalidateOwnedUpdateAsync(scenario.Plan, scenario.Update,
            scenario.Context, CancellationToken.None);

        (await retry.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeBackup);
        scenario.Payload.Opened.Should().HaveCount(openedPayloads);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
    }

    [Fact]
    public async Task OwnedUpdate_ReceiptByteDriftAfterPreparation_RefusesBeforeSnapshot()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        string receiptPath = scenario.ReceiptPath;
        File.AppendAllText(receiptPath, " ");
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeBackup = scenario.BackupHashes();

        Func<Task> commit = () => scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        (await commit.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeBackup);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+build")]
    [InlineData("0.9.9")]
    public async Task SelectOwnedUpdate_NonNewerReleaseVersion_RefusesWithoutMutation(string version)
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        BasicTemplateArtifact candidate = scenario.After with { Version = NuGetVersion.Parse(version) };
        BasicCompanionPlan plan = scenario.Plan with
        {
            Versions = new(candidate.Version, true, candidate.Version, true),
            Candidates = [candidate],
        };
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        int openedPayloads = scenario.Payload.Opened.Count;

        Func<Task> select = () => scenario.Lifecycle.SelectOwnedUpdateAsync(plan, candidate,
            scenario.Update.Before.SelectedProjectIdentity!, scenario.Update.Before.Owner,
            scenario.Update.Before.WorkloadInventory, scenario.Context, CancellationToken.None);

        (await select.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.Payload.Opened.Should().HaveCount(openedPayloads);
    }

    [Fact]
    public async Task SelectOwnedUpdate_SourceSwitch_RefusesWithoutMutation()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        BasicTemplateArtifact candidate = scenario.After with { Source = "https://different.invalid/v3/index.json" };
        BasicCompanionPlan plan = scenario.Plan with { Candidates = [candidate] };
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        int openedPayloads = scenario.Payload.Opened.Count;

        Func<Task> select = () => scenario.Lifecycle.SelectOwnedUpdateAsync(plan, candidate,
            scenario.Update.Before.SelectedProjectIdentity!, scenario.Update.Before.Owner,
            scenario.Update.Before.WorkloadInventory, scenario.Context, CancellationToken.None);

        (await select.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.Payload.Opened.Should().HaveCount(openedPayloads);
    }

    [Fact]
    public async Task SelectOwnedUpdate_SelectedProjectNotInIncumbentInventory_Refuses()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        BasicTemplateArtifact candidate = scenario.After with
        {
            Templates = [new("project/.template.config/template.json", "Fixture.Other.Project", "project")],
            ContentRoles = [new("Fixture.Other.Project", NativeTemplateContentRole.AdditionalProject)],
        };
        BasicCompanionPlan plan = scenario.Plan with { Candidates = [candidate] };
        int openedPayloads = scenario.Payload.Opened.Count;

        Func<Task> select = () => scenario.Lifecycle.SelectOwnedUpdateAsync(plan, candidate,
            "Fixture.Other.Project", scenario.Update.Before.Owner, scenario.Update.Before.WorkloadInventory,
            scenario.Context, CancellationToken.None);

        (await select.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        scenario.Payload.Opened.Should().HaveCount(openedPayloads);
    }

    [Fact]
    public async Task OwnedUpdate_PreparedArchiveByteDrift_RefusesBeforeSnapshot()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        File.AppendAllText(prepared.Archive!, "staged archive drift");
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeBackup = scenario.BackupHashes();

        Func<Task> commit = () => scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        (await commit.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeBackup);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
    }

    [Fact]
    public async Task OwnedUpdate_PreparedArchiveSwappedWhileWaitingForWriter_RefusesBeforeSnapshot()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        PhysicalFileSystem files = new();
        IFileSystem admissionFiles = Substitute.For<IFileSystem>();
        admissionFiles.FileExists(Arg.Any<string>()).Returns(call => files.FileExists(call.Arg<string>()));
        admissionFiles.DirectoryExists(Arg.Any<string>()).Returns(call => files.DirectoryExists(call.Arg<string>()));
        admissionFiles.CreateTempDirectory(Arg.Any<string>()).Returns(call => files.CreateTempDirectory(call.Arg<string>()));
        admissionFiles.CopyDirectoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => files.CopyDirectoryAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>()));
        admissionFiles.When(value => value.DeleteDirectory(scenario.Fixture.Templates.Backup))
            .Do(_ => throw new InvalidOperationException("Snapshot must not begin or reach a mutation engine."));
        TemplateStore guardedStore = new(new(Path.Combine(scenario.Fixture.Root, "cli-home")), admissionFiles,
            new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        WriterWaitStore store = new(guardedStore);
        BasicTemplateLifecycle lifecycle = new(store, scenario.Payload, new BasicArtifactInspector(files), scenario.Owner, files);
        using PreparedBasicTemplate prepared = await lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        Directory.CreateDirectory(scenario.Fixture.Templates.Backup);
        File.WriteAllText(Path.Combine(scenario.Fixture.Templates.Backup, "prior-backup.txt"), "Admission must not replace this backup.");
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeBackup = scenario.BackupHashes();
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        using TemplateEngineSession blocker = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, cancellation.Token);

        Task<bool> pending = lifecycle.CommitAsync(prepared, scenario.Context, cancellation.Token);
        try
        {
            await store.Waiting.Task.WaitAsync(cancellation.Token);
            pending.IsCompleted.Should().BeFalse();
            File.AppendAllText(prepared.Archive!, "swapped after initial validation");
        }
        finally
        {
            blocker.Dispose();
        }
        Func<Task> commit = async () => await pending;

        (await commit.Should().ThrowAsync<BasicAcquisitionException>()).Which.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeBackup, "refusal must precede snapshot and provider mutation, not restore afterward");
        admissionFiles.DidNotReceive().DeleteDirectory(scenario.Fixture.Templates.Backup);
        await admissionFiles.DidNotReceive().CopyDirectoryAsync(Arg.Any<string>(), scenario.Fixture.Templates.Backup, Arg.Any<CancellationToken>());
        await admissionFiles.DidNotReceive().WriteAllTextAsync(scenario.Fixture.Templates.Journal, Arg.Any<string>(), Arg.Any<CancellationToken>());
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
        using TemplateEngineSession reader = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Should().ContainSingle().Which.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task OwnedUpdate_PendingJournalAfterPreparation_RefusesBeforeRecoveryOrSnapshot()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        PhysicalFileSystem files = new();
        files.CopyDirectory(scenario.Fixture.Templates.Hive, scenario.Fixture.Templates.Backup);
        string marker = Path.Combine(scenario.Fixture.Templates.Hive, "uncommitted-update.txt");
        File.WriteAllText(marker, "Preserve the pending update state.");
        File.WriteAllText(scenario.Fixture.Templates.Journal, "pending");
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        Dictionary<string, string> beforeBackup = scenario.BackupHashes();
        byte[] beforeJournal = File.ReadAllBytes(scenario.Fixture.Templates.Journal);

        Func<Task> commit = () => scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        await commit.Should().ThrowAsync<TemplateStoreRecoveryRequiredException>();
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        scenario.BackupHashes().Should().BeEquivalentTo(beforeBackup);
        File.ReadAllBytes(scenario.Fixture.Templates.Journal).Should().Equal(beforeJournal);
    }

    [Fact]
    public async Task OwnedUpdate_ValidationFailureAfterProviderRegistration_RestoresWholeHive()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        BasicArtifactInspector inner = new(new PhysicalFileSystem());
        var failingInspector = new FailAfterUpdateValidationInspector(inner);
        var lifecycle = new BasicTemplateLifecycle(scenario.Fixture.Templates, scenario.Payload, failingInspector,
            scenario.Owner, new PhysicalFileSystem());
        using PreparedBasicTemplate prepared = await lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        string beforeReceipt = File.ReadAllText(scenario.ReceiptPath);

        Func<Task> commit = () => lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*after provider registration*");
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        File.ReadAllText(scenario.ReceiptPath).Should().Be(beforeReceipt);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
        using TemplateEngineSession reader = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Should().ContainSingle().Which.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task OwnedUpdate_CancellationAfterProviderRegistration_RestoresWholeHive()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        BasicArtifactInspector inner = new(new PhysicalFileSystem());
        var failingInspector = new FailAfterUpdateValidationInspector(inner, cancellation);
        var lifecycle = new BasicTemplateLifecycle(scenario.Fixture.Templates, scenario.Payload, failingInspector,
            scenario.Owner, new PhysicalFileSystem());
        using PreparedBasicTemplate prepared = await lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        string beforeReceipt = File.ReadAllText(scenario.ReceiptPath);

        Func<Task> commit = () => lifecycle.CommitAsync(prepared, scenario.Context, cancellation.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        File.ReadAllText(scenario.ReceiptPath).Should().Be(beforeReceipt);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
        using TemplateEngineSession reader = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Should().ContainSingle().Which.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task OwnedUpdate_PartialReceiptWriteFailure_RestoresArchiveProviderCacheAndReceipt()
    {
        using OwnedUpdateScenario scenario = await OwnedUpdateScenario.CreateAsync();
        using PreparedBasicTemplate prepared = await scenario.Lifecycle.PrepareAsync(
            new(scenario.Update.After, false) { Update = scenario.Update }, scenario.Context, false, CancellationToken.None);
        Dictionary<string, string> beforeHive = scenario.Fixture.HiveHashes();
        string beforeReceipt = File.ReadAllText(scenario.ReceiptPath);
        scenario.FailReceiptWriteAfterPersist = true;

        Func<Task> commit = () => scenario.Lifecycle.CommitAsync(prepared, scenario.Context, CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*owned receipt write failure*");
        scenario.Fixture.HiveHashes().Should().BeEquivalentTo(beforeHive);
        File.ReadAllText(scenario.ReceiptPath).Should().Be(beforeReceipt);
        File.Exists(scenario.Fixture.Templates.Journal).Should().BeFalse();
        using TemplateEngineSession reader = await scenario.Fixture.Templates.OpenReadAsync(scenario.Context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Should().ContainSingle().Which.Version.Should().Be("1.0.0");
    }

    private sealed class FailAfterUpdateValidationInspector(BasicArtifactInspector inner, CancellationTokenSource? cancellation = null)
        : IBasicArtifactInspector, IBasicOwnedArchiveInspector
    {
        private int _afterValidations;

        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await inner.ValidateAsync(archive, artifact, cancellationToken);
            if (artifact.Version.ToNormalizedString() == "1.0.1" && Interlocked.Increment(ref _afterValidations) == 3)
            {
                if (cancellation is not null)
                {
                    await cancellation.CancelAsync();
                    return;
                }
                throw new IOException("Injected failure after provider registration.");
            }
        }

        public Task<BasicTemplateArchiveInventory> InspectOwnedArchiveAsync(string archive, NativeTemplateOwnerReceipt receipt,
            CancellationToken cancellationToken)
            => inner.InspectOwnedArchiveAsync(archive, receipt, cancellationToken);
    }

    private sealed class WriterWaitStore(ITemplateStore inner) : ITemplateStore
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TemplateEngineSession> OpenReadAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            TemplateConstraintEvaluationSnapshot? snapshot = null, Action<TemplateStoreWaitReason>? waiting = null)
            => inner.OpenReadAsync(context, cancellationToken, snapshot, waiting);

        public Task<TemplateStoreWriteSession> OpenWriteAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null, bool requireCleanStore = false)
            => inner.OpenWriteAsync(context, cancellationToken, reason =>
            {
                Waiting.TrySetResult();
                waiting?.Invoke(reason);
            }, requireCleanStore);

        public bool IsPathInHive(string path) => inner.IsPathInHive(path);
    }

    private static BasicTemplateArtifact NativeArtifact(BasicAcquisitionFixture fixture, string version = "1.0.0")
        => fixture.Artifact(version) with { ContentRoles = [new("Fixture.Basic.Item", NativeTemplateContentRole.Function)] };

    private sealed class OwnedUpdateScenario : IDisposable
    {
        private readonly PhysicalFileSystem _files = new();
        private bool _disposed;

        private OwnedUpdateScenario()
        {
            Fixture = new BasicAcquisitionFixture();
            Context = new(WorkingDirectory.FromExplicit(Fixture.Root)) { RequireCleanStore = true };
            Payload = new(_payloadArchives);
            IFileSystem receiptFiles = Substitute.For<IFileSystem>();
            receiptFiles.OpenReadIfExists(Arg.Any<string>()).Returns(call => _files.OpenReadIfExists(call.Arg<string>()));
            receiptFiles.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                await _files.WriteAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>());
                if (FailReceiptWriteAfterPersist && string.Equals(call.ArgAt<string>(0), ReceiptPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    throw new IOException("The owned receipt write failure occurred after persistence.");
                }
            });
            Owner = new(receiptFiles, new(Path.Combine(Fixture.Root, "cli-home")));
            Lifecycle = new(Fixture.Templates, Payload, new BasicArtifactInspector(_files), Owner, _files);
            Old = CreateArtifact("1.0.0", "old package content");
            After = CreateArtifact("1.0.1", "new package content");
            _payloadArchives[$"{Old.PackageId}/{Old.Version.ToNormalizedString()}"] = ArchivePath(Old.Version.ToNormalizedString());
            _payloadArchives[$"{After.PackageId}/{After.Version.ToNormalizedString()}"] = ArchivePath(After.Version.ToNormalizedString());
        }

        private readonly Dictionary<string, string> _payloadArchives = new(StringComparer.OrdinalIgnoreCase);

        public BasicAcquisitionFixture Fixture { get; }
        public TemplateEngineContext Context { get; }
        public FixturePayloadSource Payload { get; }
        public NativeTemplateOwnership Owner { get; }
        public BasicTemplateLifecycle Lifecycle { get; }
        public BasicTemplateArtifact Old { get; }
        public BasicTemplateArtifact After { get; }
        public BasicOwnedTemplateUpdate Update { get; private set; } = null!;
        public BasicCompanionPlan Plan { get; private set; } = null!;
        public string ReceiptPath => Path.Combine(Fixture.Templates.Hive, ".native-template-owners", "fixture.update.json");
        public bool FailReceiptWriteAfterPersist { get; set; }

        public Dictionary<string, string> BackupHashes()
            => Directory.Exists(Fixture.Templates.Backup)
                ? Directory.GetFiles(Fixture.Templates.Backup, "*", SearchOption.AllDirectories).ToDictionary(
                    path => Path.GetRelativePath(Fixture.Templates.Backup, path),
                    path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                : [];

        public static async Task<OwnedUpdateScenario> CreateAsync()
        {
            OwnedUpdateScenario scenario = new();
            try
            {
                await scenario.InitializeAsync();
                return scenario;
            }
            catch
            {
                scenario.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Fixture.Dispose();
            }
        }

        private async Task InitializeAsync()
        {
            await Lifecycle.EnsureAsync(Old, Context, false, CancellationToken.None);
            NativeTemplateOwnerIdentity owner = new("Fixture.Stack", "1.0.0", BasicAcquisitionFixture.Source);
            string inventory = "selected-owner-workload-inventory";
            Plan = new(After.PackageId, new(After.Version, true, After.Version, true), [After]);
            BasicTemplateSelection selected = await Lifecycle.SelectOwnedUpdateAsync(Plan, After,
                "Fixture.Update.Project", owner, inventory, Context, CancellationToken.None);
            Update = selected.Update ?? throw new InvalidOperationException("The fixture did not produce an owned update proof.");
        }

        private BasicTemplateArtifact CreateArtifact(string version, string content)
        {
            string archive = ArchivePath(version);
            using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "Fixture.Update.nuspec",
                    $$"""<?xml version="1.0"?><package><metadata><id>Fixture.Update</id><version>{{version}}</version><authors>fixture</authors><description>Owned update fixture.</description><packageTypes><packageType name="FuncTemplate" /></packageTypes></metadata></package>""");
                WriteEntry(zip, "project/.template.config/template.json",
                    """{"identity":"Fixture.Update.Project","name":"Owned update project","shortName":"fixture-update","tags":{"type":"project"}}""");
                WriteEntry(zip, "project/.template.config/func.host.json", "{}");
                WriteEntry(zip, "project/marker.txt", content);
            }

            string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)));
            return new("Fixture.Update", NuGetVersion.Parse(version), BasicAcquisitionFixture.Source, digest,
                $"fixture-{version}", [new("project/.template.config/template.json", "Fixture.Update.Project", "project")])
            {
                ContentRoles = [new("Fixture.Update.Project", NativeTemplateContentRole.AdditionalProject)],
            };
        }

        private string ArchivePath(string version) => Path.Combine(Fixture.Root, $"Fixture.Update.{version}.nupkg");

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            using StreamWriter writer = new(archive.CreateEntry(path).Open());
            writer.Write(content);
        }
    }
}
