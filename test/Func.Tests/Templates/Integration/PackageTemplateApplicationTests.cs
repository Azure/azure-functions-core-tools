// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Preparation;
using Azure.Functions.Cli.Tests.Templates.Selection;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class PackageTemplateApplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_FixtureProject_PreservesExactCandidateCanonicalParametersAndRetainedBytes(bool reverse)
    {
        var fixture = new PackageTemplateFixture();
        var message = SelectionFixtures.Parameter("Message", defaultValue: "default") with { LongName = "message" };
        var enabled = SelectionFixtures.Parameter("Enabled", "bool", "true") with { LongName = "enabled" };
        var mode = SelectionFixtures.Parameter("Mode", "choice") with
        {
            LongName = "mode",
            Choices = [new("Debug", null, null), new("Release", null, null)],
        };
        var chosen = fixture.Candidate(identity: "Fixture.Exact", parameters: [message, enabled, mode]);
        var other = fixture.Candidate(identity: "Fixture.Other");
        fixture.SetCandidates(reverse ? [chosen, other] : [other, chosen]);
        var request = fixture.Request(reference: "Fixture.Exact", tokens: ["--message=", "--enabled=false", "--mode", "debug"], name: "FixtureApp");

        await using var operation = await fixture.Application.PrepareAsync(request, CancellationToken.None);

        chosen.Arguments!.ValuesByCanonicalName.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Message"] = "",
            ["Enabled"] = "false",
            ["Mode"] = "Debug",
        });
        chosen.Arguments.ExplicitParameters.Should().BeEquivalentTo(["Message", "Enabled", "Mode"]);
        chosen.Name.Should().Be("FixtureApp");
        other.RenderCalls.Should().Be(0);
        fixture.Lease!.PackageId.Should().Be(TemplatePackageTestData.PackageId);
        fixture.Lease.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(fixture.ArchiveBytes)));
        using var retained = fixture.Lease.OpenRead();
        SHA256.HashData(retained).Should().Equal(SHA256.HashData(fixture.ArchiveBytes));
        operation.Preview().Identity.TemplateIdentity.Should().Be("Fixture.Exact");
        operation.Preview().Identity.PackageId.Should().Be(fixture.Lease.PackageId);
        operation.Preview().Identity.PackageVersion.Should().Be(fixture.Lease.Version);
        operation.PackageSha256.Should().Be(fixture.Lease.Sha256);
        operation.Preview().Compatibility.Should().BeNull();
        fixture.Storage.Files.Should().BeEmpty();
        fixture.ProjectResolver.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_FixtureProject_PublishesCapturedOutputAndConfigurationWithoutRenderingAgain()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        fixture.SetCandidates(candidate);
        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);
        var original = operation.Preview();
        candidate.Capture = new([new("changed.txt", PreparedOutputKind.File, "not retained"u8)]);

        var completion = await operation.CommitAsync(CancellationToken.None);

        completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        completion.ConfigurationCompleted.Should().BeTrue();
        fixture.Storage.Files[PreparedOperationTestFileSystem.Destination("app.bin")].Should().Equal([0, 1, 255]);
        fixture.Storage.Files[PreparedOperationTestFileSystem.Destination(".func/config.json")]
            .Should().Equal(original.Configuration!.Entry.Content);
        fixture.Storage.Files.Should().NotContainKey(PreparedOperationTestFileSystem.Destination("changed.txt"));
        candidate.RenderCalls.Should().Be(1);
        operation.Preview().Should().BeSameAs(original);
        fixture.Storage.FileSystem.Received(1).OpenReadIfExists(TemplatePackageTestData.ArchivePath);
    }

    [Fact]
    public async Task PrepareAsync_AuthoredDefaultSuppliedExplicitly_RetainsExplicitProvenance()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(parameters: [SelectionFixtures.Parameter("Message", defaultValue: "same")]);
        fixture.SetCandidates(candidate);

        await using var operation = await fixture.Application.PrepareAsync(
            fixture.Request(tokens: ["--Message", "same"]), CancellationToken.None);

        candidate.Arguments!.ValuesByCanonicalName["Message"].Should().Be("same");
        candidate.Arguments.ExplicitParameters.Should().Contain("Message");
    }

    [Theory]
    [InlineData("missing", "capability-missing")]
    [InlineData("unsupported", "capability-unsupported")]
    [InlineData("conflicting", "capability-conflicting")]
    public async Task PrepareAsync_ResolvedFixtureProjectDoesNotSupportItem_RejectsBeforeRender(
        string scenario, string diagnostic)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(candidate);
        ProjectCapability[] reports = scenario switch
        {
            "missing" => [],
            "unsupported" => [new(PackageTemplateFixture.Capability, ProjectCapabilitySupport.Unsupported)],
            _ => [new(PackageTemplateFixture.Capability, ProjectCapabilitySupport.Supported),
                new(PackageTemplateFixture.Capability, ProjectCapabilitySupport.Unsupported)],
        };
        fixture.Project = new(reports);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectIncompatible);
        failure.Compatibility!.Diagnostics.Should().Contain(entry => entry.Code == diagnostic);
        candidate.RenderCalls.Should().Be(0);
        fixture.Storage.Files.Should().BeEmpty();
        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
    }

    [Theory]
    [InlineData("publisher.future-model.http")]
    [InlineData("azure.functions.node.model-v4.javascript-commonjs")]
    public async Task PrepareAsync_SyntheticResolvedProjectSupportsOpaqueRequirement_RetainsTheCapturedProject(string capability)
    {
        var fixture = new PackageTemplateFixture();
        fixture.Project = new([new(capability, ProjectCapabilitySupport.Supported)]);
        var candidate = fixture.Candidate(TemplateType.Item, requirement: capability);
        fixture.SetCandidates(candidate);
        var request = fixture.Request(TemplateType.Item);
        using var cancellation = new CancellationTokenSource();

        await using var operation = await fixture.Application.PrepareAsync(request, cancellation.Token);
        var completion = await operation.CommitAsync(cancellation.Token);

        completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Preview().Compatibility!.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        operation.Preview().Compatibility!.Requirements.CapabilityIds.Should().Equal(capability);
        operation.Preview().Compatibility!.Project.Should().BeSameAs(fixture.Project);
        fixture.AdapterToken.Should().Be(cancellation.Token);
        candidate.RenderToken.Should().Be(cancellation.Token);
        fixture.Context!.Project!.Stack.Should().Be(fixture.Project.StackName);
        fixture.Context.Project.Language.Should().Be(fixture.Project.Language);
        await fixture.ProjectResolver.Received(1).ResolveProjectAsync(
            Arg.Is<ProjectResolutionContext>(context => context.WorkingDirectory.Info.FullName == request.TargetDirectory
                && context.InspectCapabilities), cancellation.Token);
        fixture.Storage.Files.Should().ContainKey(PreparedOperationTestFileSystem.Destination("functions/http.js"));
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("requirements")]
    public async Task PrepareAsync_CapabilitiesOrRequirementsAbsent_RejectsWithoutRendering(string missing)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item, requirement: missing == "requirements" ? null : PackageTemplateFixture.Capability);
        fixture.SetCandidates(candidate);
        if (missing == "capabilities")
        {
            fixture.Project = new([]);
        }

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectIncompatible);
        failure.Compatibility!.Diagnostics.Should().Contain(entry =>
            entry.Code == (missing == "capabilities" ? "capability-missing" : "requirements-absent"));
        candidate.RenderCalls.Should().Be(0);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ResolverProjectReplacedDuringRendering_RetainsTheOriginalObservation()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(candidate);
        var observed = fixture.Project;
        candidate.DuringRender = _ =>
        {
            fixture.Project = new([]);
            return Task.CompletedTask;
        };

        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        operation.Preview().Compatibility!.Project.Should().BeSameAs(observed);
        operation.Preview().Compatibility!.PassesProjectCapabilityCheck.Should().BeTrue();
        candidate.RenderCalls.Should().Be(1);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_ResolverProjectReplacedAfterPreview_DoesNotResolveAgain()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(candidate);
        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);
        var observed = fixture.Project;
        fixture.Project = new([]);

        var completion = await operation.CommitAsync(CancellationToken.None);

        completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Preview().Compatibility!.Project.Should().BeSameAs(observed);
        await fixture.ProjectResolver.Received(1).ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>());
        var repeat = () => operation.CommitAsync(CancellationToken.None);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
        candidate.RenderCalls.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_NewObservationSupportsItem_DoesNotChangeTheEarlierIncompatibleResult()
    {
        var fixture = new PackageTemplateFixture();
        fixture.Project = new([]);
        var first = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(first);
        var oldRequest = fixture.Request(TemplateType.Item);
        var rejected = () => fixture.Application.PrepareAsync(oldRequest, CancellationToken.None);
        var oldCompatibility = (await rejected.Should().ThrowAsync<TemplatePreparationException>()).Which.Compatibility!;
        fixture.Project = new();
        var next = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(next);
        var newRequest = fixture.Request(TemplateType.Item);

        await using var operation = await fixture.Application.PrepareAsync(newRequest, CancellationToken.None);

        oldCompatibility.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        operation.Preview().Compatibility!.Project.Should().BeSameAs(fixture.Project).And.NotBeSameAs(oldCompatibility.Project);
        first.RenderCalls.Should().Be(0);
        next.RenderCalls.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_ProjectNotResolved_PreservesDiagnosticAndNeverOpensAdapter()
    {
        var fixture = new PackageTemplateFixture();
        fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProjectResolutionResult>(new ProjectResolutionResult.NotResolved("Install the fixture runtime.")));

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectNotResolved);
        failure.Message.Should().Be("Install the fixture runtime.");
        fixture.Adapter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_NormalProjectResolver_RequestsInspectionAndUsesTheCapturedProject()
    {
        var fixture = new PackageTemplateFixture();
        var chosen = fixture.Candidate(TemplateType.Item, identity: "Fixture.ChosenItem");
        var other = fixture.Candidate(TemplateType.Item, identity: "Fixture.OtherItem");
        fixture.SetCandidates(other, chosen);
        var factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectCreationResults.Created(fixture.Project, "Fixture workload project."));
        var resolver = new FunctionsProjectResolver([new(TestWorkloads.CreateInfo("Fixture.Runtime"), factory)]);
        var application = new PackageTemplateApplication(
            new(fixture.Storage.FileSystem, TemplatePackageTestData.Limits()), fixture.Adapter, new(new()),
            resolver, new(), fixture.Storage.FileSystem);
        var request = fixture.Request(TemplateType.Item, reference: chosen.Descriptor.Metadata.Identity);

        await using var operation = await application.PrepareAsync(request, CancellationToken.None);

        operation.Preview().Compatibility!.Project.Should().BeSameAs(fixture.Project);
        chosen.RenderCalls.Should().Be(1);
        other.RenderCalls.Should().Be(0);
        await factory.Received(1).TryCreateProjectAsync(
            Arg.Is<ProjectCreationContext>(context => context.WorkingDirectory.Info.FullName == request.TargetDirectory
                && context.InspectCapabilities),
            CancellationToken.None);
        operation.Preview().Identity.TemplateIdentity.Should().Be(chosen.Descriptor.Metadata.Identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveProjectAsync_ForwardsOnlyTheRequestedInspectionIntent(bool inspect)
    {
        var fixture = new PackageTemplateFixture();
        var factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectCreationResults.Created(fixture.Project, "Fixture project."));
        var resolver = new FunctionsProjectResolver([new(TestWorkloads.CreateInfo("Fixture.Runtime"), factory)]);
        var context = new ProjectResolutionContext(fixture.Project.WorkingDirectory) { InspectCapabilities = inspect };

        var result = await resolver.ResolveProjectAsync(context, CancellationToken.None);

        result.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Should().BeSameAs(fixture.Project);
        new ProjectCreationContext(fixture.Project.WorkingDirectory).InspectCapabilities.Should().BeFalse();
        new ProjectResolutionContext(fixture.Project.WorkingDirectory).InspectCapabilities.Should().BeFalse();
        await factory.Received(1).TryCreateProjectAsync(
            Arg.Is<ProjectCreationContext>(creation => creation.InspectCapabilities == inspect), CancellationToken.None);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task PrepareAsync_ProjectInspectionFailure_PropagatesWithoutOpeningNativeStateOrReturningEmptyReports(string kind)
    {
        var fixture = new PackageTemplateFixture();
        Exception cause = kind switch
        {
            "format" => new FormatException("Malformed project input."),
            "io" => new IOException("Project inspection failed."),
            _ => new UnauthorizedAccessException("Project input is unreadable."),
        };
        fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProjectResolutionResult>(cause));

        var failure = await Record.ExceptionAsync(() => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None));

        failure.Should().BeSameAs(cause);
        fixture.Adapter.ReceivedCalls().Should().BeEmpty();
        fixture.Storage.Files.Should().BeEmpty();
        fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectResolutionResult.Resolved(fixture.Project, "New observation."));
        fixture.SetCandidates(fixture.Candidate(TemplateType.Item));
        await using var next = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);
        next.Preview().Compatibility!.PassesProjectCapabilityCheck.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareAsync_WorkloadResolutionFailure_RetainsTheOriginalFailureObject()
    {
        var fixture = new PackageTemplateFixture();
        var workerFailure = FunctionsWorkerResolutionFailures.NotInstalled(new("fixture-runtime"), "Install the fixture worker.");
        var projectFailure = ProjectCreationFailures.WorkerNotResolved(workerFailure, workerFailure.Message);
        fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProjectResolutionResult>(new ProjectResolutionResult.NotResolved(projectFailure.Message, projectFailure)));

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.ProjectFailure.Should().BeSameAs(projectFailure);
        fixture.Adapter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ResolvedProjectHasDifferentRoot_DoesNotPrepareForAnotherProject()
    {
        var fixture = new PackageTemplateFixture();
        fixture.Project = new() { Root = Path.Combine(PreparedOperationTestFileSystem.Target, "another-project") };

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectMismatch);
        fixture.Adapter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ResolverProjectReplacedDuringAdapterOpen_UsesTheOriginalObservation()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(candidate);
        var observed = fixture.Project;
        fixture.Open = (_, _, _) =>
        {
            fixture.Project = new() { Root = Path.Combine(PreparedOperationTestFileSystem.Target, "changed-project") };
            return Task.FromResult<PackageTemplateSession>(fixture.Session);
        };

        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        operation.Preview().Compatibility!.Project.Should().BeSameAs(observed);
        candidate.RenderCalls.Should().Be(1);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-found", "NotFound")]
    [InlineData("wrong-type", "WrongType")]
    [InlineData("ambiguous", "AmbiguousGroup")]
    [InlineData("arguments", "InvalidArguments")]
    public async Task PrepareAsync_SelectionRejects_PreservesItsExactStatusAndDoesNotRender(
        string scenario, string expected)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(scenario == "wrong-type" ? TemplateType.Item : TemplateType.Project);
        fixture.SetCandidates(candidate);
        var reference = scenario == "not-found" ? "absent" : candidate.Descriptor.Metadata.ShortNames[0];
        if (scenario == "ambiguous")
        {
            var other = candidate.Descriptor.Metadata with { Identity = "Fixture.Other", GroupIdentity = "another-group" };
            fixture.Session = new([candidate, new FixtureCandidate(new(other), FuncTemplateConstraintOutcome.Eligible)]);
        }

        var request = fixture.Request(reference: reference, tokens: scenario == "arguments" ? ["--unknown", "value"] : []);
        var action = () => fixture.Application.PrepareAsync(request, CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.SelectionRejected);
        failure.Selection!.Status.Should().Be(Enum.Parse<TemplateSelectionStatus>(expected));
        candidate.RenderCalls.Should().Be(0);
        fixture.Session.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_RequiredInputMissing_DoesNotConfuseCompletenessWithSelection()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(parameters: [SelectionFixtures.Parameter("Required", required: true)]);
        fixture.SetCandidates(candidate);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.MissingRequiredInput);
        failure.Selection!.Status.Should().Be(TemplateSelectionStatus.Selected);
        candidate.RenderCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Restricted")]
    [InlineData("NotEvaluated")]
    public async Task PrepareAsync_NativeEligibilityRejects_DoesNotTreatSelectionAsExecutionAuthority(string eligibility)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item, eligibility: Enum.Parse<FuncTemplateConstraintOutcome>(eligibility));
        fixture.SetCandidates(candidate);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.NativeIneligible);
        failure.Diagnostics.Should().Equal(candidate.Diagnostics);
        candidate.RenderCalls.Should().Be(0);
    }
}
