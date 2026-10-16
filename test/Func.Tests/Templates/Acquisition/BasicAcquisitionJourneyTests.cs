// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed class BasicAcquisitionJourneyTests
{
    [Fact]
    public async Task RunAsync_FreshThenRepeat_InstallsRealPointerAndNativeCompanionAndReusesBoth()
    {
        using var fixture = new BasicAcquisitionFixture();

        var fresh = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        var before = fixture.HiveHashes();
        var repeated = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        fresh.Status.Should().Be(BasicAcquisitionStatus.Completed);
        fresh.Revision.Should().Be("test-reviewed-basic-cohort-1");
        fresh.Completed.Should().BeEquivalentTo(new BasicAcquisitionStep[]
        {
            new("Fixture.StackA", "1.0.0", false), new("Fixture.Basic", "1.0.0", false),
        });
        var persisted = await new WorkloadStore(fixture.Paths).GetWorkloadsAsync();
        persisted.Should().ContainSingle().Which.LogicalPackage!.PackageId.Should().Be("fixture.stacka");
        persisted[0].PackageId.Should().Be("fixture.stacka.test-rid");
        File.ReadAllText(Path.Combine(fixture.Paths.GetInstallDirectory(persisted[0].PackageId, persisted[0].PackageVersion),
            "tools", "any", "fixture.txt")).Should().Be("Installation fixture, not an activated stack implementation.");
        (await fixture.ReadTemplatesAsync()).Should().ContainSingle().Which.Identity.Should().Be("Fixture.Basic.Item");
        repeated.Status.Should().Be(BasicAcquisitionStatus.Completed);
        repeated.Completed.Should().OnlyContain(step => step.Reused);
        fixture.Downloads.Should().HaveCount(2, "only the first request acquires the pointer and its physical implementation");
        fixture.Payload.Opened.Should().Equal("1.0.0");
        fixture.HiveHashes().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_RetainedStackAfterCompanionFailureOrCancellation_ReopenedBRejectsDisjointRangeBeforeMutation(bool cancel)
    {
        using var fixture = new BasicAcquisitionFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Payload.Missing = !cancel;
        fixture.Payload.Opening = _ =>
        {
            if (cancel)
            {
                cancellation.Cancel();
            }
        };

        var first = await fixture.Runner().RunAsync(fixture.Request(), cancellation.Token);
        var downloads = fixture.Downloads.Count;
        var bytes = fixture.HiveHashes();
        fixture.Payload.Missing = false;
        fixture.Payload.Opening = null;
        var reopened = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);

        first.Status.Should().Be(cancel ? BasicAcquisitionStatus.Canceled : BasicAcquisitionStatus.PartialFailure);
        first.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        reopened.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        reopened.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().BeEmpty();
        fixture.HiveHashes().Should().BeEquivalentTo(bytes);
        (await new WorkloadStore(fixture.Paths).GetWorkloadsAsync()).Should().ContainSingle()
            .Which.LogicalPackage!.PackageId.Should().Be("fixture.stacka");
    }

    [Fact]
    public async Task RunAsync_FailureThenRepair_ReusesRetainedStackWithoutSuccessReceipt()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.Payload.Missing = true;
        var failed = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        fixture.Payload.Missing = false;

        var repaired = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        failed.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        repaired.Status.Should().Be(BasicAcquisitionStatus.Completed);
        repaired.Completed[0].Reused.Should().BeTrue();
        fixture.Downloads.Should().HaveCount(2);
        (await fixture.ReadTemplatesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_UnselectedSideBySideStackRows_AllRetainTheirRequirements()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var first = (await fixture.Store.GetWorkloadsAsync()).Single();
        // Persist an independent second version without asking the pointer installer to move the first logical owner.
        await fixture.Store.SaveWorkloadAsync(new WorkloadEntry
        {
            PackageId = "fixture.stacka.test-rid",
            PackageVersion = "2.0.0",
            LogicalPackage = new() { PackageId = "Fixture.StackA", PackageVersion = "2.0.0", Source = "C:\\local\\stack.nupkg" },
            Kind = first.Kind,
        });
        var downloads = fixture.Downloads.Count;

        var result = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackC"), CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
        (await fixture.Store.GetWorkloadsAsync()).Should().HaveCount(2);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_RemovalThenReinstall_ReconstructsRequirementsFromPresentRowsOnly()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        await fixture.Installer.UninstallAsync("Fixture.StackA", "1.0.0", WorkloadOwnershipKind.Logical);

        var b = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var downloads = fixture.Downloads.Count;
        var reinstated = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);

        b.Status.Should().Be(BasicAcquisitionStatus.Completed);
        b.Completed[1].Version.Should().Be("2.0.0");
        reinstated.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().Equal("2.0.0");
    }

    [Fact]
    public async Task RunAsync_CompatibleOlderQualifiedUserPin_ReusesInsteadOfChoosingNewest()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        await fixture.Installer.UninstallAsync("Fixture.StackA", "1.0.0", WorkloadOwnershipKind.Logical);
        fixture.Ownership.Entries["1.0.0"] = new(BasicAcquisitionFixture.Source, "1.0.0");

        var result = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackC"), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Completed);
        result.Completed[1].Should().Be(new BasicAcquisitionStep("Fixture.Basic", "1.0.0", true));
        fixture.Payload.Opened.Should().Equal("1.0.0");
    }

    [Theory]
    [InlineData("https://different.invalid/v3/index.json", null)]
    [InlineData(BasicAcquisitionFixture.Source, "2.0.0")]
    public async Task RunAsync_ActualOwnershipSourceOrPinConflicts_StopsBeforeStackMutation(string source, string? pin)
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        fixture.Ownership.Entries["1.0.0"] = new(source, pin);
        var before = fixture.HiveHashes();
        var downloads = fixture.Downloads.Count;

        var result = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackC"), CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.InstalledConflict);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.HiveHashes().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task RunAsync_OwnershipInputUnavailable_FailsClosedInsteadOfInferringFromVersion()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        var downloads = fixture.Downloads.Count;

        var result = await fixture.Runner(ownership: new UnavailableBasicTemplateOwnership())
            .RunAsync(fixture.Request("Fixture.StackC"), CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.OwnershipUnavailable);
        fixture.Downloads.Should().HaveCount(downloads);
    }

    [Theory]
    [InlineData("https://different.invalid/v3/index.json")]
    [InlineData("C:\\local\\feed")]
    public async Task RunAsync_DisallowedOrInvalidExplicitSource_RejectsBeforeAnyInstall(string source)
    {
        using var fixture = new BasicAcquisitionFixture();
        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSource(source).Returns(_ => fixture.SourceProvider.GetSource(source));

        var result = await fixture.Runner(source: sourceProvider).RunAsync(fixture.Request() with { Source = source }, CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
        fixture.Downloads.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
        sourceProvider.Received(1).GetSource(source);
    }

    [Fact]
    public async Task RunAsync_ExplicitThenEnvironmentThenDefault_UsesExistingPrecedence()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.SourceProvider = new(Options.Create(new WorkloadCatalogOptions { Source = BasicAcquisitionFixture.Source }));
        var environment = await fixture.Runner().RunAsync(fixture.Request(check: true) with { Source = null }, CancellationToken.None);
        var explicitSource = await fixture.Runner().RunAsync(fixture.Request(check: true) with { Source = "https://different.invalid/v3/index.json" }, CancellationToken.None);
        fixture.SourceProvider = new(Options.Create(new WorkloadCatalogOptions()));
        var defaultSource = await fixture.Runner().RunAsync(fixture.Request(check: true) with { Source = null }, CancellationToken.None);

        environment.Source.Should().Be(BasicAcquisitionFixture.Source);
        environment.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        explicitSource.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
        defaultSource.Source.Should().Be(PackageSourceProvider.DefaultSourceUrl);
        defaultSource.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
        fixture.Downloads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_OfflineMissingPayload_ReportsIncompleteOrPartialWithoutDownload(bool stackPresent)
    {
        using var fixture = new BasicAcquisitionFixture();
        if (stackPresent)
        {
            await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        }

        var downloads = fixture.Downloads.Count;

        var result = await fixture.Runner().RunAsync(fixture.Request(offline: true), CancellationToken.None);

        result.Status.Should().Be(stackPresent ? BasicAcquisitionStatus.PartialFailure : BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.PayloadUnavailable);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().BeEmpty();
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_OfflineVerifiedInstalledContent_SucceedsWithoutAcquisition()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        var downloads = fixture.Downloads.Count;

        var result = await fixture.Runner().RunAsync(fixture.Request(offline: true), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Completed);
        result.Completed.Should().OnlyContain(step => step.Reused);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().Equal("1.0.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_UnapprovedCuratedContent_FailsDigestOrCompleteBasicInventoryEvenWhenBytesAreApproved(bool approveBytes)
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.ReplaceBasicWithCuratedContent(approveBytes);

        var result = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        result.Failure.Should().Be(BasicAcquisitionFailure.InvalidArtifact);
        result.Message.Should().Contain(approveBytes ? "inventory" : "digest");
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
        fixture.HiveHashes().Should().BeEmpty();
        (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ProviderAcquiresDifferentBytes_ActualValidationRestoresHiveAndKeepsStack()
    {
        using var fixture = new BasicAcquisitionFixture();
        var physical = new BasicArtifactInspector(new PhysicalFileSystem());
        var inspector = Substitute.For<IBasicArtifactInspector>();
        var validations = 0;
        inspector.ValidateAsync(Arg.Any<string>(), Arg.Any<BasicTemplateArtifact>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (++validations == 3)
                {
                    using (var zip = ZipFile.Open(call.Arg<string>(), ZipArchiveMode.Update))
                    {
                        using var text = new StreamWriter(zip.CreateEntry("unexpected.txt").Open());
                        text.Write("unapproved actual content");
                    }
                }

                await physical.ValidateAsync(call.Arg<string>(), call.Arg<BasicTemplateArtifact>(), call.Arg<CancellationToken>());
            });

        var result = await fixture.Runner(inspector).RunAsync(fixture.Request(), CancellationToken.None);

        validations.Should().Be(3, "staging, private native preflight and actual committed-candidate archive are checked");
        result.Failure.Should().Be(BasicAcquisitionFailure.InvalidArtifact);
        result.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        fixture.HiveHashes().Should().BeEmpty();
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
        (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_CancelAfterNativeInstall_StoreRollsBackAndRetainedStackCanBeRepaired()
    {
        using var fixture = new BasicAcquisitionFixture();
        var physical = new BasicArtifactInspector(new PhysicalFileSystem());
        var inspector = Substitute.For<IBasicArtifactInspector>();
        using var cancellation = new CancellationTokenSource();
        var validations = 0;
        inspector.ValidateAsync(Arg.Any<string>(), Arg.Any<BasicTemplateArtifact>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await physical.ValidateAsync(call.Arg<string>(), call.Arg<BasicTemplateArtifact>(), call.Arg<CancellationToken>());
                if (++validations == 3)
                {
                    cancellation.Cancel();
                }
            });

        var canceled = await fixture.Runner(inspector).RunAsync(fixture.Request(), cancellation.Token);
        var restored = fixture.HiveHashes();
        var repaired = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        canceled.Status.Should().Be(BasicAcquisitionStatus.Canceled);
        canceled.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        restored.Should().BeEmpty();
        repaired.Status.Should().Be(BasicAcquisitionStatus.Completed);
        fixture.Downloads.Should().HaveCount(2);
        (await fixture.ReadTemplatesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_CheckNonStackAndBrowsing_HaveNoPackageWrites()
    {
        using var fixture = new BasicAcquisitionFixture();

        var check = await fixture.Runner().RunAsync(fixture.Request(check: true), CancellationToken.None);
        var nonStack = await fixture.Runner().RunAsync(fixture.Request("Fixture.Worker"), CancellationToken.None);
        var browse = await fixture.ReadTemplatesAsync();

        check.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        nonStack.Status.Should().Be(BasicAcquisitionStatus.NotApplicable);
        browse.Should().BeEmpty();
        fixture.HiveHashes().Should().BeEmpty();
        fixture.Downloads.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_LiteralLocalStackSource_DoesNotBecomeCompanionFeed()
    {
        using var fixture = new BasicAcquisitionFixture();
        var row = new WorkloadEntry { PackageId = "Fixture.StackA", PackageVersion = "1.0.0", Source = "C:\\local\\stack.nupkg" };
        await fixture.Store.SaveWorkloadAsync(row);
        Directory.CreateDirectory(fixture.Paths.GetInstallDirectory(row.PackageId, row.PackageVersion));

        var result = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Completed);
        result.Source.Should().Be(BasicAcquisitionFixture.Source);
        result.Completed[0].Reused.Should().BeTrue();
        fixture.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnknownHistoricalVersionOrMissingExactApproval_FailsBeforeDeployment()
    {
        using var fixture = new BasicAcquisitionFixture();
        var historical = await fixture.Runner().RunAsync(fixture.Request(version: "9.0.0"), CancellationToken.None);
        JsonNode mapping = JsonNode.Parse(fixture.Catalog.Json)!;
        mapping["artifacts"]!.AsArray().Clear();
        fixture.Catalog.Json = mapping.ToJsonString();

        var missing = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        historical.Status.Should().Be(BasicAcquisitionStatus.Unavailable);
        historical.Message.Should().Contain("historical");
        missing.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
        fixture.Downloads.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ProductionBundleUnavailable_DoesNotPretendAnEmptyArtifactIsSuccess()
    {
        using var fixture = new BasicAcquisitionFixture();
        var production = new BundledBasicCompanionCatalog(new BasicCompanionManifestReader());

        var result = await fixture.Runner(catalog: production).RunAsync(fixture.Request(), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Unavailable);
        result.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
        result.Message.Should().Contain("publisher-qualified");
        fixture.Downloads.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_VerifiedOfflineLocalPayload_UsesRealNativeInstallWithoutFeedAccess()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var downloads = fixture.Downloads.Count;
        var local = Substitute.For<IBasicPackagePayloadSource>();
        local.OpenAsync(Arg.Any<BasicTemplateArtifact>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => (Stream)File.OpenRead(fixture.Archive()));

        var result = await fixture.Runner(payload: local).RunAsync(fixture.Request(offline: true), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Completed);
        result.Completed[0].Reused.Should().BeTrue();
        result.Completed[1].Reused.Should().BeFalse();
        fixture.Downloads.Should().HaveCount(downloads);
        (await fixture.ReadTemplatesAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("future-version")]
    [InlineData("bad-constraints")]
    [InlineData("undiscoverable")]
    [InlineData("wrong-package-type")]
    [InlineData("malformed-version")]
    [InlineData("malformed-xml")]
    public async Task RunAsync_ApprovedBytesDoNotBypassMetadataAndPrivateNativePreflight(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        using (var zip = ZipFile.Open(fixture.Archive(), ZipArchiveMode.Update))
        {
            string path = change is "future-version" or "wrong-package-type" or "malformed-version" or "malformed-xml"
                ? "Fixture.Basic.nuspec" : "basic/.template.config/template.json";
            var entry = zip.GetEntry(path)!;
            string content;
            using (var text = new StreamReader(entry.Open()))
            {
                content = text.ReadToEnd();
            }

            entry.Delete();
            content = change switch
            {
                "future-version" => content.Replace("<version>1.0.0</version>", "<version>1.1.0</version>"),
                "wrong-package-type" => content.Replace("FuncTemplate", "FuncCliWorkload"),
                "malformed-version" => content.Replace("<version>1.0.0</version>", "<version>bad</version>"),
                "malformed-xml" => "<package><broken",
                "bad-constraints" => content.Replace("\"sourceName\"", "\"constraints\":{\"required\":{\"type\":\"func-workload\",\"args\":[]}},\"sourceName\""),
                "undiscoverable" => content.Replace("\"fixture-basic-item\"", "[]"),
                _ => throw new InvalidOperationException("Unexpected fixture change."),
            };
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(content);
        }

        var document = JsonNode.Parse(fixture.Catalog.Json)!;
        document["artifacts"]![0]!["sha256"] = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.Archive()))).ToLowerInvariant();
        fixture.Catalog.Json = document.ToJsonString();

        var result = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        result.Failure.Should().BeOneOf(BasicAcquisitionFailure.InvalidArtifact, BasicAcquisitionFailure.InstallFailed);
        result.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
        fixture.HiveHashes().Should().BeEmpty();
        if (change is "malformed-version" or "malformed-xml")
        {
            result.Failure.Should().Be(BasicAcquisitionFailure.InvalidArtifact);
            result.Cause.Should().BeOfType(change == "malformed-version" ? typeof(ArgumentException) : typeof(System.Xml.XmlException));
            var downloads = fixture.Downloads.Count;
            var reopened = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);
            reopened.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
            fixture.Downloads.Should().HaveCount(downloads);
            fixture.Payload.Opened.Should().Equal("1.0.0");
        }
    }

    [Fact]
    public async Task RunAsync_CancellationBeforePlanning_ProducesNoPackageMutations()
    {
        using var fixture = new BasicAcquisitionFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await fixture.Runner().RunAsync(fixture.Request(), cancellation.Token);

        result.Status.Should().Be(BasicAcquisitionStatus.Canceled);
        result.Completed.Should().BeEmpty();
        fixture.Downloads.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_LegacyTemplatesWorkload_IsNotNativeFuncTemplateRegistration()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        await fixture.Store.SaveWorkloadAsync(new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.Templates.Node", PackageVersion = "1.0.0" });

        var check = await fixture.Runner().RunAsync(fixture.Request(check: true), CancellationToken.None);

        check.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        fixture.Payload.Opened.Should().BeEmpty();
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("protocol")]
    [InlineData("timeout")]
    public async Task RunAsync_ExpectedStackTransportFailure_ReturnsTypedDiagnosticWithoutTemplateMutation(string failure)
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.StackTransportFailure = failure switch
        {
            "http" => new HttpRequestException("Fixture stack feed unreachable."),
            "protocol" => new NuGet.Protocol.Core.Types.FatalProtocolException("Fixture stack feed protocol failed."),
            "timeout" => new TaskCanceledException("Fixture stack transport timeout."),
            _ => throw new InvalidOperationException("Unknown fixture failure."),
        };

        var result = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.InstallFailed);
        result.Message.Should().Contain(fixture.StackTransportFailure.Message);
        result.Source.Should().Be(BasicAcquisitionFixture.Source);
        result.Cause.Should().BeSameAs(fixture.StackTransportFailure);
        result.Completed.Should().BeEmpty();
        fixture.Payload.Opened.Should().BeEmpty();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_NonSourceArgumentFailure_IsNotRelabeledByParameterName()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.StackTransportFailure = new ArgumentException("Fixture programmer error, not a feed override.", "value");

        Func<Task> run = () => fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        await run.Should().ThrowAsync<ArgumentException>().WithMessage("*programmer error*");
        fixture.Payload.Opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CompanionTransportAfterStackPersistence_RetainsProgressAndBlocksReopenedB(bool timeout)
    {
        using var fixture = new BasicAcquisitionFixture();
        Exception failure = timeout ? new TaskCanceledException("Fixture companion timeout.") : new HttpRequestException("Fixture companion feed failed.");
        fixture.Payload.Opening = _ => throw failure;

        var partial = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        var downloads = fixture.Downloads.Count;
        fixture.Payload.Opening = null;
        var reopened = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);

        partial.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        partial.Failure.Should().Be(BasicAcquisitionFailure.PayloadUnavailable);
        partial.Source.Should().Be(BasicAcquisitionFixture.Source);
        partial.Cause.Should().BeSameAs(failure);
        partial.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        reopened.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
        fixture.Downloads.Should().HaveCount(downloads);
        fixture.Payload.Opened.Should().BeEmpty();
        fixture.HiveHashes().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TargetedBWithIndependentU_DoesNotRestoreRetainedAOnlyT_ExplicitARepairDoes()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.Payload.Missing = true;
        var partialA = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        var retainedA = (await new WorkloadStore(fixture.Paths).GetWorkloadsAsync()).Single();
        fixture.UseIndependentBCompanion();
        fixture.Payload.Missing = false;

        var requestedB = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);
        var afterBDownloads = fixture.Downloads.Count;
        var repeatedB = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);
        var afterBTemplates = await fixture.ReadTemplatesAsync();
        var requestedAfterB = fixture.Payload.Requested.ToArray();
        var repairedA = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);

        partialA.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        partialA.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        requestedB.Status.Should().Be(BasicAcquisitionStatus.Completed, "completion describes B's targeted operation, not all machine companions");
        requestedB.Completed.Select(step => step.PackageId).Should().Equal("Fixture.StackB", "Fixture.Unrelated");
        repeatedB.Status.Should().Be(BasicAcquisitionStatus.Completed);
        repeatedB.Completed.Should().OnlyContain(step => step.Reused);
        requestedAfterB.Should().Equal("Fixture.Unrelated/1.0.0");
        afterBTemplates.Should().ContainSingle().Which.Identity.Should().Be("Fixture.Unrelated.Item");
        repairedA.Status.Should().Be(BasicAcquisitionStatus.Completed);
        repairedA.Completed.Should().BeEquivalentTo(new BasicAcquisitionStep[]
        {
            new("Fixture.StackA", "1.0.0", true), new("Fixture.Basic", "1.0.0", false),
        });
        fixture.Payload.Requested.Should().Equal("Fixture.Unrelated/1.0.0", "Fixture.Basic/1.0.0");
        fixture.Downloads.Should().HaveCount(afterBDownloads, "repeat B and explicit A repair reuse their persisted stack rows");
        var finalRows = await new WorkloadStore(fixture.Paths).GetWorkloadsAsync();
        finalRows.Should().HaveCount(2);
        finalRows.Single(row => row.LogicalPackage!.PackageId == "fixture.stacka").Should().BeEquivalentTo(retainedA);
        (await fixture.ReadTemplatesAsync()).Select(template => template.Identity).Should().BeEquivalentTo(
            ["Fixture.Unrelated.Item", "Fixture.Basic.Item"]);
    }
}
