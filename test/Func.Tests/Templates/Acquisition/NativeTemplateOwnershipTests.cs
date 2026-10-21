// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
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

    private static BasicTemplateArtifact NativeArtifact(BasicAcquisitionFixture fixture, string version = "1.0.0")
        => fixture.Artifact(version) with { ContentRoles = [new("Fixture.Basic.Item", NativeTemplateContentRole.Function)] };
}
