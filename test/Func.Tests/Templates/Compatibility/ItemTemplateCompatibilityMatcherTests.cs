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

        var result = await _matcher.EvaluateAsync(project, requirements, _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        result.PassesProjectCapabilityCheck.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
        result.Context.ProjectRoot.Should().Be(Root);
        result.Context.Authority.Should().Be(project.StackIdentity);
        result.Context.CaptureId.Should().Be(_captureId);
        result.RequirementsFingerprint.Should().Be(requirements.Fingerprint);
        result.ProjectFactsFingerprint.Should().Be(result.ProjectFacts!.Fingerprint);
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

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(isIncompatible ? ItemTemplateCompatibility.Incompatible : ItemTemplateCompatibility.Unknown);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(diagnostic);
    }

    [Fact]
    public async Task EvaluateAsync_UnsupportedAndMissingOrUnknownCapabilities_IsIncompatible()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("b-unsupported", ProjectCapabilitySupport.Unsupported), new("c-unknown", ProjectCapabilitySupport.Unknown)]);

        var result = await _matcher.EvaluateAsync(project, new(["c-unknown", "a-missing", "b-unsupported"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            "capability-missing", "capability-unsupported", "capability-unknown");
    }

    [Fact]
    public async Task EvaluateAsync_MissingRequiredCapability_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("unrelated", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-missing");
    }

    [Fact]
    public async Task EvaluateAsync_StackReturnsNoFacts_IsUnknown()
    {
        var project = new TestProject { Capture = (_, _) => ValueTask.FromResult<ProjectFacts?>(null) };

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.ProjectFactsFingerprint.Should().BeNull();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("project-facts-missing");
    }

    [Fact]
    public async Task EvaluateAsync_StackDoesNotOverrideFacts_IsUnknown()
    {
        var project = new LegacyProject();

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal("project-facts-missing");
    }

    [Fact]
    public async Task EvaluateAsync_AbsentRequirements_IsUnknownEvenWithAuthoritativeFacts()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, ItemTemplateRequirements.Parse(null), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("requirements-absent");
    }

    [Fact]
    public async Task EvaluateAsync_ConflictingRequiredEvidence_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("future.http", ProjectCapabilitySupport.Supported), new("future.http", ProjectCapabilitySupport.Unsupported)]);

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-conflicting");
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

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-conflicting");
    }

    [Fact]
    public async Task EvaluateAsync_MultipleUnknownFactsWithoutUnsupported_IsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("a", ProjectCapabilitySupport.Unknown), new("b", ProjectCapabilitySupport.Unknown)]);

        var result = await _matcher.EvaluateAsync(project, new(["b", "a"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Select(diagnostic => diagnostic.CapabilityId).Should().Equal("a", "b");
        result.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Code == "capability-unknown");
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    [InlineData(ProjectCapabilitySupport.Unknown)]
    public async Task EvaluateAsync_RepeatedConsistentReports_PreserveSupportOutcome(ProjectCapabilitySupport support)
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("future.http", support), new("future.http", support)]);

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        ItemTemplateCompatibility expected = support switch
        {
            ProjectCapabilitySupport.Supported => ItemTemplateCompatibility.Compatible,
            ProjectCapabilitySupport.Unsupported => ItemTemplateCompatibility.Incompatible,
            ProjectCapabilitySupport.Unknown => ItemTemplateCompatibility.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(support)),
        };
        result.Compatibility.Should().Be(expected);
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "capability-conflicting");
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unknown)]
    [InlineData(ProjectCapabilitySupport.Unknown, ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Unknown)]
    [InlineData(ProjectCapabilitySupport.Unknown, ProjectCapabilitySupport.Unsupported)]
    public async Task EvaluateAsync_DifferingReportsIncludingUnknown_InvalidateTheSnapshot(
        ProjectCapabilitySupport first,
        ProjectCapabilitySupport second)
    {
        var project = new TestProject();
        project.Capabilities.AddRange(
            [new("required", ProjectCapabilitySupport.Unsupported), new("unrequired", first), new("unrequired", second)]);

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Be(new ItemTemplateCompatibilityDiagnostic(
                "capability-conflicting", "The stack reports conflicting project evidence for capability 'unrequired'.", "unrequired"));
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    public async Task EvaluateAsync_InsufficientAuthority_IsUnknownRatherThanTrustingPositiveOrNegativeFacts(ProjectCapabilitySupport support)
    {
        var project = new TestProject { IsAuthoritative = false };
        project.Capabilities.Add(new("future.http", support));

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("project-facts-not-authoritative");
    }

    [Fact]
    public async Task EvaluateAsync_MissingStackAuthority_IsUnknownEvenWhenSnapshotClaimsAuthority()
    {
        var project = new TestProject { StackIdentity = null };
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-authority-missing");
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

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(diagnostic);
    }

    [Fact]
    public async Task EvaluateAsync_NewOperationCannotReusePriorCapture()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));
        var first = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);
        project.Capture = (_, _) => ValueTask.FromResult(first.ProjectFacts);

        var second = await _matcher.EvaluateAsync(project, new(["future.http"]), _otherCaptureId, CancellationToken.None);

        first.PassesProjectCapabilityCheck.Should().BeTrue();
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
            project.StackIdentity = changedPart == "authority" ? "another.owner" : context.Authority;
            return ValueTask.FromResult<ProjectFacts?>(new(context, new('A', 64), true, [new("a", ProjectCapabilitySupport.Supported)]));
        };

        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-changed");
    }

    [Fact]
    public async Task EvaluateAsync_CallerMutationAfterReturn_DoesNotAlterResult()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("future.http", ProjectCapabilitySupport.Supported));
        List<string> ids = ["future.http"];
        var result = await _matcher.EvaluateAsync(project, new(ids), _captureId, CancellationToken.None);
        var factsFingerprint = result.ProjectFactsFingerprint;
        var requirementsFingerprint = result.RequirementsFingerprint;

        project.Capabilities.Clear();
        ids.Clear();
        project.StackIdentity = "changed.owner";
        project.ProjectRoot = Path.Combine(Root, "changed");

        result.PassesProjectCapabilityCheck.Should().BeTrue();
        result.Context.ProjectRoot.Should().Be(Root);
        result.Context.Authority.Should().Be("example.runtime");
        result.ProjectFacts!.Capabilities.Should().ContainSingle();
        result.Requirements.CapabilityIds.Should().Equal("future.http");
        result.ProjectFactsFingerprint.Should().Be(factsFingerprint);
        result.RequirementsFingerprint.Should().Be(requirementsFingerprint);
        Action changeDiagnostics = () => ((IList<ItemTemplateCompatibilityDiagnostic>)result.Diagnostics).Clear();
        changeDiagnostics.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task EvaluateAsync_CapabilityIdsAreCaseSensitive()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("Future.Http", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, new(["future.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
    }

    [Fact]
    public async Task EvaluateAsync_UnfamiliarStackAndCapability_NeedsNoStackSpecificPolicy()
    {
        var project = new TestProject { StackIdentity = "unfamiliar.runtime-2042" };
        project.Capabilities.Add(new("publisher_2042.unknown-model.http", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, new(["publisher_2042.unknown-model.http"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Theory]
    [InlineData(".NET")]
    [InlineData("example stack")]
    [InlineData("future.\u03bb")]
    public async Task EvaluateAsync_CanonicalStackIdentity_DoesNotUseCapabilityIdentifierGrammar(string stack)
    {
        var project = new TestProject { StackIdentity = stack };
        project.Capabilities.Add(new("required", ProjectCapabilitySupport.Supported));

        var result = await _matcher.EvaluateAsync(project, new(["required"]), _captureId, CancellationToken.None);

        result.Context.Authority.Should().Be(stack);
        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        result.PassesProjectCapabilityCheck.Should().BeTrue();
    }

    [Theory]
    [InlineData("node")]
    [InlineData("python")]
    [InlineData("dotnet")]
    [InlineData("5.0.0")]
    public async Task EvaluateAsync_StackOrPackageLikeLabelWithoutFacts_DoesNotEstablishCompatibility(string label)
    {
        var project = new TestProject { StackIdentity = label };

        var result = await _matcher.EvaluateAsync(project, new(["required.semantic-capability"]), _captureId, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
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
    public async Task EvaluateAsync_PreCanceled_DoesNotCallStack()
    {
        var project = new TestProject();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = () => _matcher.EvaluateAsync(project, new(["a"]), _captureId, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        project.CaptureCount.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_StackCancelsAfterCapture_DoesNotReturnResult()
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
    public async Task EvaluateAsync_StackFailure_IsNotSilentlyConvertedToMissingFacts()
    {
        var project = new TestProject { Capture = (_, _) => throw new IOException("Stack evidence is unreadable.") };

        Func<Task> action = () => _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        await action.Should().ThrowAsync<IOException>().WithMessage("Stack evidence is unreadable.");
    }

    [Fact]
    public async Task RevalidateAsync_UnchangedEvidence_StillPassesProjectCapabilityCheck()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        var current = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);

        current.PassesProjectCapabilityCheck.Should().BeTrue();
        current.ProjectFactsFingerprint.Should().Be(result.ProjectFactsFingerprint);
        project.CaptureCount.Should().Be(2);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("capabilities")]
    [InlineData("authority-quality")]
    [InlineData("facts-disappeared")]
    public async Task RevalidateAsync_ChangedProjectEvidence_InvalidatesPriorResult(string changedPart)
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
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

        var current = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.PassesProjectCapabilityCheck.Should().BeFalse();
        current.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-facts-changed");
        result.PassesProjectCapabilityCheck.Should().BeTrue();
    }

    [Theory]
    [InlineData("root")]
    [InlineData("authority")]
    public async Task RevalidateAsync_ChangedProjectIdentity_InvalidatesPriorResult(string changedPart)
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.ProjectRoot = changedPart == "root" ? Path.Combine(Root, "other") : Root;
        project.StackIdentity = changedPart == "authority" ? "other.owner" : project.StackIdentity;

        var current = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain("project-changed");
    }

    [Fact]
    public async Task RevalidateAsync_ChangedRequirements_InvalidatesPriorResultEvenWhenNewRequirementsAreSupported()
    {
        var project = new TestProject();
        project.Capabilities.AddRange([new("a", ProjectCapabilitySupport.Supported), new("b", ProjectCapabilitySupport.Supported)]);
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);

        var current = await _matcher.RevalidateAsync(result, project, new(["b"]), CancellationToken.None);

        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("requirements-changed");
    }

    [Fact]
    public async Task RevalidateAsync_UnknownDecision_CannotBePromotedWithoutNewEvaluation()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Unknown));
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.Capabilities[0] = new("a", ProjectCapabilitySupport.Supported);

        var revalidated = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);
        var newDecision = await _matcher.EvaluateAsync(project, new(["a"]), _otherCaptureId, CancellationToken.None);

        revalidated.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        newDecision.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Fact]
    public async Task RevalidateAsync_ProjectChangesDuringRecaptureWithSameSnapshot_RemainsUnknown()
    {
        var project = new TestProject();
        project.Capabilities.Add(new("a", ProjectCapabilitySupport.Supported));
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.Capture = (_, _) =>
        {
            project.StackIdentity = "changed.owner";
            return ValueTask.FromResult(result.ProjectFacts);
        };

        var current = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);

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
            project.StackIdentity = "changed.owner";
            return ValueTask.FromResult<ProjectFacts?>(new(context, new('A', 64), true, project.Capabilities));
        };
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        project.StackIdentity = "example.runtime";
        project.Capture = (_, _) => ValueTask.FromResult(result.ProjectFacts);

        var current = await _matcher.RevalidateAsync(result, project, new(["a"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.ProjectFactsFingerprint.Should().Be(result.ProjectFactsFingerprint);
        current.Compatibility.Should().Be(ItemTemplateCompatibility.Unknown);
        current.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("admission-outcome-changed");
    }

    [Fact]
    public async Task Parse_MalformedDeclarationArgument_ThrowsBeforeStackCapture()
    {
        var project = new TestProject();
        Func<Task> action = () => _matcher.EvaluateAsync(project,
            ItemTemplateRequirements.Parse("""{"type":"func-project-capabilities","args":[]}"""), _captureId, CancellationToken.None);

        await action.Should().ThrowAsync<FormatException>();
        project.CaptureCount.Should().Be(0);
    }

    [Fact]
    public async Task RevalidateAsync_PreCanceled_DoesNotCallStackAgain()
    {
        var project = new TestProject();
        var result = await _matcher.EvaluateAsync(project, new(["a"]), _captureId, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = () => _matcher.RevalidateAsync(result, project, new(["a"]), cancellation.Token);

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
        Func<Task> nullResult = () => _matcher.RevalidateAsync(null!, project, new(["a"]), CancellationToken.None);

        await nullProject.Should().ThrowAsync<ArgumentNullException>().WithParameterName("project");
        await nullRequirements.Should().ThrowAsync<ArgumentNullException>().WithParameterName("requirements");
        await emptyCapture.Should().ThrowAsync<ArgumentException>().WithParameterName("captureId");
        await nullResult.Should().ThrowAsync<ArgumentNullException>().WithParameterName("previousResult");
    }

    private class LegacyProject : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory => WorkingDirectory.FromExplicit(Root);

        public override string StackName => "example.runtime";

        public override string StackDisplayName => throw new InvalidOperationException("Compatibility matching must not read display names.");

        public override string? Language => throw new InvalidOperationException("Compatibility matching must not read language labels.");

        public override bool SupportsExtensionBundles => throw new InvalidOperationException("Compatibility matching must not read bundle policy.");

        public override FunctionsWorkerReference WorkerReference => throw new InvalidOperationException("Compatibility matching must not read worker references.");
    }

    private sealed class TestProject : LegacyProject
    {
        public string ProjectRoot { get; set; } = Root;

        public string? StackIdentity { get; set; } = "example.runtime";

        public string EvidenceFingerprint { get; set; } = new('A', 64);

        public bool IsAuthoritative { get; set; } = true;

        public List<ProjectCapability> Capabilities { get; } = [];

        public Func<ProjectFactsContext, CancellationToken, ValueTask<ProjectFacts?>>? Capture { get; set; }

        public int CaptureCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public override WorkingDirectory WorkingDirectory => WorkingDirectory.FromExplicit(ProjectRoot);

        public override string StackName => StackIdentity!;

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
