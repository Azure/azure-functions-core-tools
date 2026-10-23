// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Templates.Compatibility;

public sealed class ItemTemplateCompatibilityMatcherTests
{
    private static readonly Guid _captureId = new("42db8847-ec89-44ac-b380-c41a28ebef62");
    private static readonly Guid _otherCaptureId = new("e5335115-88c5-44fd-a7a2-67dff33a354d");
    private readonly ItemTemplateCompatibilityMatcher _matcher = new();

    private static string Root => Path.DirectorySeparatorChar == '\\' ? @"C:\projects\sample" : "/projects/sample";

    [Fact]
    public async Task EvaluateAsync_AllRequiredCapabilitiesSupported_IsCompatible()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("future.model", ProjectCapabilitySupport.Supported), new("future.http", ProjectCapabilitySupport.Supported)]);
        var requirements = new ItemTemplateRequirements(["future.model", "future.http"]);

        var admission = await _matcher.EvaluateAsync(project, requirements, _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        admission.IsEligibleForPreparation.Should().BeTrue();
        admission.Diagnostics.Should().BeEmpty();
        admission.Context.ProjectRoot.Should().Be(Root);
        admission.Context.Authority.Should().Be(project.Authority);
        admission.Context.CaptureId.Should().Be(_captureId);
        admission.RequirementsFingerprint.Should().Be(requirements.Fingerprint);
        admission.ProjectFactsFingerprint.Should().Be(admission.ProjectFacts!.Fingerprint);
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Unsupported, true, "capability-unsupported")]
    [InlineData(ProjectCapabilitySupport.Unknown, false, "capability-unknown")]
    public async Task EvaluateAsync_RequiredCapabilityNotSupported_FailsClosed(
        ProjectCapabilitySupport support,
        bool isIncompatible,
        string diagnostic)
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", support));

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(isIncompatible ? ItemTemplateCompatibility.Incompatible : ItemTemplateCompatibility.Unknown);
        admission.IsEligibleForPreparation.Should().BeFalse();
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(diagnostic);
    }

    [Fact]
    public async Task EvaluateAsync_UnsupportedAndMissingOrUnknownCapabilities_IsIncompatible()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("b-unsupported", ProjectCapabilitySupport.Unsupported), new("c-unknown", ProjectCapabilitySupport.Unknown)]);

        var admission = await _matcher.EvaluateAsync(project, new(["c-unknown", "a-missing", "b-unsupported"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        admission.IsEligibleForPreparation.Should().BeFalse();
        admission.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            "capability-missing", "capability-unsupported", "capability-unknown");
    }

    [Fact]
    public async Task EvaluateAsync_MissingRequiredCapability_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("unrelated", ProjectCapabilitySupport.Supported));

        var admission = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-missing");
    }

    [Fact]
    public async Task EvaluateAsync_RuntimeReturnsNoFacts_IsUnknown()
    {
        var project = new TestProject { Capture = (_, _) => ValueTask.FromResult<ProjectFacts?>(null) };

        var admission = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.ProjectFactsFingerprint.Should().BeNull();
        admission.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("project-facts-missing");
    }

    [Fact]
    public async Task EvaluateAsync_RuntimeDoesNotOverrideFacts_IsUnknown()
    {
        var project = new LegacyProject();

        var admission = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("project-authority-missing", "project-facts-missing");
    }

    [Fact]
    public async Task EvaluateAsync_AbsentRequirements_IsUnknownEvenWithAuthoritativeFacts()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));

        var admission = await _matcher.EvaluateAsync(project, ItemTemplateRequirements.Parse(null), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.IsEligibleForPreparation.Should().BeFalse();
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("requirements-absent");
    }

    [Fact]
    public async Task EvaluateAsync_ConflictingRequiredEvidence_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("future.http", ProjectCapabilitySupport.Supported), new("future.http", ProjectCapabilitySupport.Unsupported)]);

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-conflicting");
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    public async Task EvaluateAsync_ConflictingSnapshotCannotEstablishSupportOrNonSupport(ProjectCapabilitySupport requiredSupport)
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
        [
            new("required", requiredSupport),
            new("other", ProjectCapabilitySupport.Supported),
            new("other", ProjectCapabilitySupport.Unsupported),
        ]);

        var admission = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.IsEligibleForPreparation.Should().BeFalse();
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-conflicting");
    }

    [Fact]
    public async Task EvaluateAsync_MultipleUnknownFactsWithoutUnsupported_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("a", ProjectCapabilitySupport.Unknown), new("b", ProjectCapabilitySupport.Unknown)]);

        var admission = await _matcher.EvaluateAsync(project, new(["b", "a"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Select(diagnostic => diagnostic.CapabilityId).Should().Equal("a", "b");
        admission.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Code == "capability-unknown");
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedConsistentFact_DoesNotCreateConflictingEvidence()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("future.http", ProjectCapabilitySupport.Supported), new("future.http", ProjectCapabilitySupport.Supported)]);

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    public async Task EvaluateAsync_InsufficientAuthority_IsUnknownRatherThanTrustingPositiveOrNegativeFacts(ProjectCapabilitySupport support)
    {
        var project = new TestProject { IsAuthoritative = false };
        project.Capabilities.Add(new("future.http", support));

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("project-facts-not-authoritative");
    }

    [Fact]
    public async Task EvaluateAsync_MissingRuntimeAuthority_IsUnknownEvenWhenSnapshotClaimsAuthority()
    {
        var project = new TestProject { Authority = null };
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-authority-missing");
    }

    [Theory]
    [InlineData("root", "project-root-mismatch")]
    [InlineData("authority", "project-authority-mismatch")]
    [InlineData("capture", "project-facts-stale")]
    public async Task EvaluateAsync_UnrelatedOrStaleEvidence_IsUnknown(string changedPart, string diagnostic)
    {
        var project = new TestProject
        {
            Capture = (context, _) => ValueTask.FromResult<ProjectFacts?>(new(
                new(
                    changedPart == "root" ? Path.Combine(context.ProjectRoot, "other") : context.ProjectRoot,
                    changedPart == "authority" ? "another.owner" : context.Authority,
                    changedPart == "capture" ? _otherCaptureId : context.CaptureId),
                new('A', 64),
                true,
                [new("future.http", ProjectCapabilitySupport.Supported)])),
        };

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.IsEligibleForPreparation.Should().BeFalse();
        admission.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(diagnostic);
    }

    [Fact]
    public async Task EvaluateAsync_NewOperationCannotReusePriorCapture()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));
        var first = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);
        project.Capture = (_, _) => ValueTask.FromResult(first.ProjectFacts);

        var second = await _matcher.EvaluateAsync(project, new(["future.http"]), _otherCaptureId, CancellationToken.None);

        first.IsEligibleForPreparation.Should().BeTrue();
        second.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        second.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("project-facts-stale");
        project.CaptureCount.Should().Be(2);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("authority")]
    public async Task EvaluateAsync_ProjectChangesDuringCapture_IsUnknown(string changedPart)
    {
        var project = new TestProject();
        project.Capture = (context, _) =>
        {
            project.ProjectRoot = changedPart == "root" ? Path.Combine(Root, "other") : Root;
            project.Authority = changedPart == "authority" ? "another.owner" : context.Authority;
            return ValueTask.FromResult<ProjectFacts?>(new(context, new('A', 64), true, [new("a", ProjectCapabilitySupport.Supported)]));
        };

        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        admission.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-changed");
    }

    [Fact]
    public async Task EvaluateAsync_CallerMutationAfterReturn_DoesNotAlterAdmission()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));
        List<string> ids = ["future.http"];
        var admission = await _matcher.EvaluateAsync(project, new(ids), _captureId, CancellationToken.None);
        var factsFingerprint = admission.ProjectFactsFingerprint;
        var requirementsFingerprint = admission.RequirementsFingerprint;

        project.Capabilities.Clear();
        ids.Clear();
        project.Authority = "changed.owner";
        project.ProjectRoot = Path.Combine(Root, "changed");

        admission.IsEligibleForPreparation.Should().BeTrue();
        admission.Context.ProjectRoot.Should().Be(Root);
        admission.Context.Authority.Should().Be("example.runtime");
        admission.ProjectFacts!.Capabilities.Should().ContainSingle();
        admission.Requirements.CapabilityIds.Should().Equal("future.http");
        admission.ProjectFactsFingerprint.Should().Be(factsFingerprint);
        admission.RequirementsFingerprint.Should().Be(requirementsFingerprint);
        Action changeDiagnostics = () => ((IList<ItemTemplateCompatibilityDiagnostic>)admission.Diagnostics).Clear();
        changeDiagnostics.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task EvaluateAsync_CapabilityIdsAreCaseSensitive()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("Future.Http", ProjectCapabilitySupport.Supported));

        var admission = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
    }

    [Fact]
    public async Task EvaluateAsync_UnfamiliarRuntimeAndCapability_NeedsNoRuntimeSpecificPolicy()
    {
        var project = new TestProject { Authority = "unfamiliar.runtime-2042" };
        project.Capabilities.Add(new("publisher_2042.unknown-model.http", ProjectCapabilitySupport.Supported));

        var admission = await _matcher.EvaluateAsync(project, new(["publisher_2042.unknown-model.http"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("python")]
    [InlineData("dotnet")]
    [InlineData("5.0.0")]
    public async Task EvaluateAsync_RuntimeOrPackageLikeLabelWithoutFacts_DoesNotEstablishCompatibility(string label)
    {
        var project = new TestProject { Authority = label };

        var admission = await _matcher.EvaluateAsync(project, new(["required.semantic-capability"]), _captureId, CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
    }

    [Fact]
    public async Task EvaluateAsync_EquivalentReorderedInputs_HaveDeterministicEvidenceAndDiagnostics()
    {
        var firstProject = new TestProject();
        firstProject.Capabilities.AddRange([new("c", ProjectCapabilitySupport.Unknown), new("b", ProjectCapabilitySupport.Unsupported)]);
        var secondProject = new TestProject();
        secondProject.Capabilities.AddRange([new("b", ProjectCapabilitySupport.Unsupported), new("c", ProjectCapabilitySupport.Unknown)]);

        var first = await _matcher.EvaluateAsync(firstProject, new(["c", "a", "b"]), _captureId, CancellationToken.None);
        var second = await _matcher.EvaluateAsync(secondProject, new(["b", "a", "c"]), _captureId, CancellationToken.None);

        first.RequirementsFingerprint.Should().Be(second.RequirementsFingerprint);
        first.ProjectFactsFingerprint.Should().Be(second.ProjectFactsFingerprint);
        first.Diagnostics.Should().Equal(second.Diagnostics);
    }

    [Fact]
    public async Task EvaluateAsync_PreCanceled_DoesNotCallRuntime()
    {
        var project = new TestProject();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = () => _matcher.EvaluateAsync(project, new(["a"]), _captureId, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        project.CaptureCount.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_RuntimeCancelsAfterCapture_DoesNotReturnAdmission()
    {
        using var cancellation = new CancellationTokenSource();
        var project = new TestProject
        {
            Capture = (context, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult<ProjectFacts?>(new(context, new('A', 64), true, [new("a", ProjectCapabilitySupport.Supported)]));
            },
        };

        Func<Task> action = () => _matcher.EvaluateAsync(project, new(["a"]), _captureId, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        project.LastToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task EvaluateAsync_RuntimeFailure_IsNotSilentlyConvertedToMissingFacts()
    {
        var project = new TestProject { Capture = (_, _) => throw new IOException("Runtime evidence is unreadable.") };

        Func<Task> action = () => _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        await action.Should().ThrowAsync<IOException>().WithMessage("Runtime evidence is unreadable.");
    }

    [Fact]
    public async Task RevalidateAsync_UnchangedEvidence_RemainsEligible()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        var current = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);

        current.IsEligibleForPreparation.Should().BeTrue();
        current.ProjectFactsFingerprint.Should().Be(admission.ProjectFactsFingerprint);
        project.CaptureCount.Should().Be(2);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("capabilities")]
    [InlineData("authority-quality")]
    [InlineData("facts-disappeared")]
    public async Task RevalidateAsync_ChangedProjectEvidence_InvalidatesPriorAdmission(string changedPart)
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        switch (changedPart)
        {
            case "evidence":
                project.EvidenceFingerprint = new('B', 64);
                break;
            case "capabilities":
                project.Capabilities[0] = new("a", ProjectCapabilitySupport.Unsupported);
                break;
            case "authority-quality":
                project.IsAuthoritative = false;
                break;
            case "facts-disappeared":
                project.Capture = (_, _) => ValueTask.FromResult<ProjectFacts?>(null);
                break;
        }

        var current = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.IsEligibleForPreparation.Should().BeFalse();
        current.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-facts-changed");
        admission.IsEligibleForPreparation.Should().BeTrue();
    }

    [Theory]
    [InlineData("root")]
    [InlineData("authority")]
    public async Task RevalidateAsync_ChangedProjectIdentity_InvalidatesPriorAdmission(string changedPart)
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.ProjectRoot = changedPart == "root" ? Path.Combine(Root, "other") : Root;
        project.Authority = changedPart == "authority" ? "other.owner" : project.Authority;

        var current = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-changed");
    }

    [Fact]
    public async Task RevalidateAsync_ChangedRequirements_InvalidatesPriorAdmissionEvenWhenNewRequirementsAreSupported()
    {
        var project = new TestProject();
        project.Capabilities.AddRange([new("a", ProjectCapabilitySupport.Supported), new("b", ProjectCapabilitySupport.Supported)]);
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        var current = await _matcher.RevalidateAsync(admission, project, new(["b"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("requirements-changed");
    }

    [Fact]
    public async Task RevalidateAsync_UnknownDecision_CannotBePromotedWithoutNewAdmission()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Unknown));
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.Capabilities[0] = new("a", ProjectCapabilitySupport.Supported);

        var revalidated = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);
        var newDecision = await _matcher.EvaluateAsync(project, new(["a"]), _otherCaptureId, CancellationToken.None);

        revalidated.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        newDecision.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Fact]
    public async Task RevalidateAsync_ProjectChangesDuringRecaptureWithSameSnapshot_RemainsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.Capture = (_, _) =>
        {
            project.Authority = "changed.owner";
            return ValueTask.FromResult(admission.ProjectFacts);
        };

        var current = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("admission-outcome-changed");
    }

    [Fact]
    public async Task RevalidateAsync_UnknownWithOtherwiseUnchangedEvidence_CannotBePromoted()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        project.Capture = (context, _) =>
        {
            project.Authority = "changed.owner";
            return ValueTask.FromResult<ProjectFacts?>(new(context, new('A', 64), true, project.Capabilities));
        };
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.Authority = "example.runtime";
        project.Capture = (_, _) => ValueTask.FromResult(admission.ProjectFacts);

        var current = await _matcher.RevalidateAsync(admission, project, new(["a"]), CancellationToken.None);

        admission.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.ProjectFactsFingerprint.Should().Be(admission.ProjectFactsFingerprint);
        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("admission-outcome-changed");
    }

    [Fact]
    public async Task EvaluateAsync_MalformedDeclaration_FailsBeforeRuntimeCapture()
    {
        var project = new TestProject();
        Func<Task> action = () => _matcher.EvaluateAsync(project,
            ItemTemplateRequirements.Parse("""{"type":"func-project-capabilities","args":[]}"""), _captureId, CancellationToken.None);

        await action.Should().ThrowAsync<FormatException>();
        project.CaptureCount.Should().Be(0);
    }

    [Fact]
    public async Task RevalidateAsync_PreCanceled_DoesNotCallRuntimeAgain()
    {
        var project = new TestProject();
        var admission = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = () => _matcher.RevalidateAsync(admission, project, new(["a"]), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        project.CaptureCount.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_InvalidArguments_Throw()
    {
        var project = new TestProject();
        Func<Task> nullProject = () => _matcher.EvaluateAsync(null!, new(["a"]), _captureId, CancellationToken.None);
        Func<Task> nullRequirements = () => _matcher.EvaluateAsync(project, null!, _captureId, CancellationToken.None);
        Func<Task> emptyCapture = () => _matcher.EvaluateAsync(project, new(["a"]), Guid.Empty, CancellationToken.None);
        Func<Task> nullAdmission = () => _matcher.RevalidateAsync(null!, project, new(["a"]), CancellationToken.None);

        await nullProject.Should().ThrowAsync<ArgumentNullException>().WithParameterName("project");
        await nullRequirements.Should().ThrowAsync<ArgumentNullException>().WithParameterName("requirements");
        await emptyCapture.Should().ThrowAsync<ArgumentException>().WithParameterName("captureId");
        await nullAdmission.Should().ThrowAsync<ArgumentNullException>().WithParameterName("admission");
    }

    private class LegacyProject : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory => WorkingDirectory.FromExplicit(Root);

        public override string StackName => throw new InvalidOperationException("Generic admission must not read stack names.");

        public override string StackDisplayName => throw new InvalidOperationException("Generic admission must not read display names.");

        public override string? Language => throw new InvalidOperationException("Generic admission must not read language labels.");

        public override bool SupportsExtensionBundles => throw new InvalidOperationException("Generic admission must not read bundle policy.");

        public override FunctionsWorkerReference WorkerReference => throw new InvalidOperationException("Generic admission must not read worker references.");
    }

    private sealed class TestProject : LegacyProject
    {
        public string ProjectRoot { get; set; } = Root;

        public string? Authority { get; set; } = "example.runtime";

        public string EvidenceFingerprint { get; set; } = new('A', 64);

        public bool IsAuthoritative { get; set; } = true;

        public List<ProjectCapability> Capabilities { get; } = [];

        public Func<ProjectFactsContext, CancellationToken, ValueTask<ProjectFacts?>>? Capture { get; set; }

        public int CaptureCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public override WorkingDirectory WorkingDirectory => WorkingDirectory.FromExplicit(ProjectRoot);

        public override string? ProjectFactsAuthority => Authority;

        public override ValueTask<ProjectFacts?> GetProjectFactsAsync(ProjectFactsContext context, CancellationToken cancellationToken)
        {
            CaptureCount++;
            LastToken = cancellationToken;
            return Capture is not null
                ? Capture(context, cancellationToken)
                : ValueTask.FromResult<ProjectFacts?>(new(context, EvidenceFingerprint, IsAuthoritative, Capabilities));
        }
    }
}
