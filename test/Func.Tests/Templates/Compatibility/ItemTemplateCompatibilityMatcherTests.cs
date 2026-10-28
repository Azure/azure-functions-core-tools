// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Templates.Compatibility;

public sealed class ItemTemplateCompatibilityMatcherTests
{
    private readonly ItemTemplateCompatibilityMatcher _matcher = new();

    [Fact]
    public void Evaluate_AllRequiredCapabilitiesSupported_IsCompatible()
    {
        var project = new TestProject(
            [new("future.model", ProjectCapabilitySupport.Supported), new("future.http", ProjectCapabilitySupport.Supported)]);
        var requirements = new ItemTemplateRequirements(["future.model", "future.http"]);

        var result = _matcher.Evaluate(project, requirements, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        result.PassesProjectCapabilityCheck.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
        result.Project.Should().BeSameAs(project);
        result.Requirements.Should().BeSameAs(requirements);
    }

    [Theory]
    [InlineData(null, null, "Incompatible")]
    [InlineData(null, ProjectCapabilitySupport.Supported, "Incompatible")]
    [InlineData(null, ProjectCapabilitySupport.Unsupported, "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Supported, null, "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Supported, "Compatible")]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported, "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Unsupported, null, "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Supported, "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Unsupported, "Incompatible")]
    public void Evaluate_RequiredCapabilityCombinations_RequireEveryCapabilitySupported(
        ProjectCapabilitySupport? first, ProjectCapabilitySupport? second, string expected)
    {
        List<ProjectCapability> capabilities = [];
        if (first.HasValue)
        {
            capabilities.Add(new("a", first.Value));
        }

        if (second.HasValue)
        {
            capabilities.Add(new("b", second.Value));
        }

        var project = new TestProject(capabilities);
        var expectedCompatibility = Enum.Parse<ItemTemplateCompatibility>(expected);

        var result = _matcher.Evaluate(project, new(["b", "a"]), CancellationToken.None);

        result.Compatibility.Should().Be(expectedCompatibility);
        result.PassesProjectCapabilityCheck.Should().Be(expectedCompatibility == ItemTemplateCompatibility.Compatible);
    }

    [Theory]
    [InlineData(null, "capability-missing")]
    [InlineData(ProjectCapabilitySupport.Unsupported, "capability-unsupported")]
    public void Evaluate_RequiredCapabilityNotSupported_IsIncompatibleWithReason(ProjectCapabilitySupport? support, string diagnosticCode)
    {
        var project = new TestProject(support.HasValue ? [new("required", support.Value)] : []);

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Be(new ItemTemplateCompatibilityDiagnostic(
                diagnosticCode,
                support.HasValue
                    ? "The project does not support required capability 'required'."
                    : "The stack provides no report for required capability 'required'.",
                "required"));
    }

    [Fact]
    public void Evaluate_UnsupportedAndMissing_IsIncompatibleWithAllDiagnostics()
    {
        var project = new TestProject([new("b-unsupported", ProjectCapabilitySupport.Unsupported)]);

        var result = _matcher.Evaluate(project, new(["a-missing", "b-unsupported"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            "capability-missing", "capability-unsupported");
    }

    [Fact]
    public void Evaluate_ExistingStackWithoutReports_IsIncompatible()
    {
        var project = new TestProject();

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-missing");
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    public void Evaluate_AbsentRequirements_IsIncompatibleRegardlessOfReportedState(ProjectCapabilitySupport support)
    {
        var project = new TestProject([new("future.http", support)]);

        var result = _matcher.Evaluate(project, ItemTemplateRequirements.Parse(null), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("requirements-absent");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"type":"func-project-capabilities","args":[]}""")]
    [InlineData("""{"type":"func-project-capabilities","args":["required","required"]}""")]
    [InlineData("""{"type":"func-workload","args":["required"]}""")]
    public void Evaluate_InvalidRequirements_IsIncompatibleWithValidationDiagnostic(string declaration)
    {
        var project = new TestProject([new("required", ProjectCapabilitySupport.Supported)]);
        var requirements = ItemTemplateRequirements.Parse(declaration);

        var result = _matcher.Evaluate(project, requirements, CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Requirements.Should().BeSameAs(requirements);
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Be(new ItemTemplateCompatibilityDiagnostic("requirements-invalid", requirements.ValidationError!));
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported, true)]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Supported, true)]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported, false)]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Supported, false)]
    public void Evaluate_ConflictingReports_InvalidateProjectCapabilitiesEvenOutsideRequirements(
        ProjectCapabilitySupport first, ProjectCapabilitySupport second, bool required)
    {
        var conflictingId = required ? "required" : "unrequired";
        var project = new TestProject(
        [
            new("required", ProjectCapabilitySupport.Unsupported),
            new(conflictingId, first),
            new(conflictingId, second),
        ]);

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Be(new ItemTemplateCompatibilityDiagnostic(
                "capability-conflicting",
                $"The language stack reports conflicting states for project capability '{conflictingId}'.",
                conflictingId));
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported, "Compatible")]
    [InlineData(ProjectCapabilitySupport.Unsupported, "Incompatible")]
    public void Evaluate_RepeatedConsistentReports_PreserveSupportOutcome(
        ProjectCapabilitySupport support, string expected)
    {
        var project = new TestProject([new("required", support), new("required", support)]);

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(Enum.Parse<ItemTemplateCompatibility>(expected));
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "capability-conflicting");
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    public void Evaluate_UnrequiredContradiction_IsIncompatibleRegardlessOfRequiredSupport(ProjectCapabilitySupport requiredSupport)
    {
        var project = new TestProject(
        [
            new("required", requiredSupport),
            new("unrequired", ProjectCapabilitySupport.Supported),
            new("unrequired", ProjectCapabilitySupport.Unsupported),
        ]);

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.CapabilityId.Should().Be("unrequired");
    }

    [Fact]
    public void Evaluate_SeveralConflictingIds_ReportsOnlyConflictsInOrdinalOrder()
    {
        ProjectCapability[] capabilities =
        [
            new("z", ProjectCapabilitySupport.Unsupported),
            new("a", ProjectCapabilitySupport.Supported),
            new("required", ProjectCapabilitySupport.Supported),
            new("z", ProjectCapabilitySupport.Supported),
            new("a", ProjectCapabilitySupport.Unsupported),
        ];
        var firstProject = new TestProject(capabilities);
        var secondProject = new TestProject(capabilities.Reverse());

        var first = _matcher.Evaluate(firstProject, new(["required", "missing"]), CancellationToken.None);
        var second = _matcher.Evaluate(secondProject, new(["missing", "required"]), CancellationToken.None);

        first.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        first.PassesProjectCapabilityCheck.Should().BeFalse();
        first.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Code == "capability-conflicting");
        first.Diagnostics.Select(diagnostic => diagnostic.CapabilityId).Should().Equal("a", "z");
        second.Diagnostics.Should().Equal(first.Diagnostics);
    }

    [Fact]
    public void Evaluate_UnrequiredUnsupportedReport_DoesNotRestrictSupportedRequirement()
    {
        var project = new TestProject(
        [
            new("required", ProjectCapabilitySupport.Supported),
            new("unrequired-negative", ProjectCapabilitySupport.Unsupported),
        ]);

        var result = _matcher.Evaluate(project, new(["required"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_CapabilityIdsAreCaseSensitive()
    {
        var project = new TestProject([new("Future.Http", ProjectCapabilitySupport.Supported)]);

        var result = _matcher.Evaluate(project, new(["future.http"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-missing");
    }

    [Fact]
    public void Evaluate_UnfamiliarCapability_DoesNotReadStackLanguageWorkerOrProjectLabels()
    {
        var project = new TestProject([new("publisher_2042.unknown-model.http", ProjectCapabilitySupport.Supported)]);

        var result = _matcher.Evaluate(project, new(["publisher_2042.unknown-model.http"]), CancellationToken.None);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
    }

    [Fact]
    public void Evaluate_ReorderedReportsAndRequirements_ProduceDeterministicDiagnostics()
    {
        var firstProject = new TestProject([new("c", ProjectCapabilitySupport.Unsupported), new("b", ProjectCapabilitySupport.Unsupported)]);
        var secondProject = new TestProject([new("b", ProjectCapabilitySupport.Unsupported), new("c", ProjectCapabilitySupport.Unsupported)]);

        var first = _matcher.Evaluate(firstProject, new(["c", "a", "b"]), CancellationToken.None);
        var second = _matcher.Evaluate(secondProject, new(["b", "a", "c"]), CancellationToken.None);

        first.Compatibility.Should().Be(second.Compatibility).And.Be(ItemTemplateCompatibility.Incompatible);
        first.Diagnostics.Should().Equal(second.Diagnostics);
    }

    [Fact]
    public void Evaluate_CallerChangesSourceCollectionsBeforeAndAfterMatching_InputsRemainCaptured()
    {
        List<ProjectCapability> capabilities = [new("required", ProjectCapabilitySupport.Supported)];
        List<string> ids = ["required"];
        var project = new TestProject(capabilities);
        var requirements = new ItemTemplateRequirements(ids);
        capabilities.Clear();
        ids.Clear();

        var result = _matcher.Evaluate(project, requirements, CancellationToken.None);
        capabilities.Add(new("required", ProjectCapabilitySupport.Unsupported));
        ids.Add("different");
        var repeated = _matcher.Evaluate(project, requirements, CancellationToken.None);

        result.PassesProjectCapabilityCheck.Should().BeTrue();
        repeated.PassesProjectCapabilityCheck.Should().BeTrue();
        result.Project.Should().BeSameAs(project);
        result.Project.Capabilities.Should().ContainSingle().Which.Support.Should().Be(ProjectCapabilitySupport.Supported);
        result.Requirements.CapabilityIds.Should().Equal("required");
    }

    [Fact]
    public void Evaluate_RepeatedMatching_DoesNotInspectProjectAgain()
    {
        var inspections = 0;
        var support = ProjectCapabilitySupport.Supported;
        IEnumerable<ProjectCapability> InspectProject()
        {
            inspections++;
            yield return new("required", support);
        }

        var project = new TestProject(InspectProject());
        var requirements = new ItemTemplateRequirements(["required"]);
        inspections.Should().Be(1);

        var first = _matcher.Evaluate(project, requirements, CancellationToken.None);
        support = ProjectCapabilitySupport.Unsupported;
        var repeated = _matcher.Evaluate(project, requirements, CancellationToken.None);

        inspections.Should().Be(1);
        first.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        repeated.Compatibility.Should().Be(first.Compatibility);
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported, "Compatible", "Incompatible")]
    [InlineData(ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Supported, "Incompatible", "Compatible")]
    public void Evaluate_NewProjectObservation_ProducesNewResultWithoutChangingOriginal(
        ProjectCapabilitySupport before, ProjectCapabilitySupport after, string originalOutcome, string newOutcome)
    {
        ProjectCapability[] inputs = [new("required", before)];
        var originalProject = new TestProject(inputs);
        var requirements = new ItemTemplateRequirements(["required"]);
        var original = _matcher.Evaluate(originalProject, requirements, CancellationToken.None);

        inputs[0] = new("required", after);
        var newProject = new TestProject(inputs);
        var current = _matcher.Evaluate(newProject, requirements, CancellationToken.None);

        original.Compatibility.Should().Be(Enum.Parse<ItemTemplateCompatibility>(originalOutcome));
        original.Project.Should().BeSameAs(originalProject);
        original.Project.Capabilities.Should().ContainSingle().Which.Support.Should().Be(before);
        current.Project.Should().BeSameAs(newProject).And.NotBeSameAs(original.Project);
        current.Compatibility.Should().Be(Enum.Parse<ItemTemplateCompatibility>(newOutcome));
    }

    [Fact]
    public void Evaluate_NewRequirements_ProducesNewResultWithoutChangingOriginal()
    {
        var project = new TestProject([new("a", ProjectCapabilitySupport.Supported), new("b", ProjectCapabilitySupport.Unsupported)]);
        var original = _matcher.Evaluate(project, new(["a"]), CancellationToken.None);

        var current = _matcher.Evaluate(project, new(["b"]), CancellationToken.None);

        original.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        original.Requirements.CapabilityIds.Should().Equal("a");
        current.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        current.Requirements.CapabilityIds.Should().Equal("b");
    }

    [Fact]
    public void Evaluate_PreCanceled_ThrowsWithoutReturningCompatibility()
    {
        var project = new TestProject();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action action = () => _matcher.Evaluate(project, new(["required"]), cancellation.Token);

        action.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        Action nullProject = () => _matcher.Evaluate(null!, new(["required"]), CancellationToken.None);
        Action nullRequirements = () => _matcher.Evaluate(new TestProject(), null!, CancellationToken.None);

        nullProject.Should().Throw<ArgumentNullException>().WithParameterName("project");
        nullRequirements.Should().Throw<ArgumentNullException>().WithParameterName("requirements");
    }

    [Fact]
    public void Result_CallerMutatesDiagnostics_CapturedDiagnosticsDoNotChange()
    {
        List<ItemTemplateCompatibilityDiagnostic> diagnostics = [new("capability-missing", "No report.", "required")];
        var result = new ItemTemplateCompatibilityResult(new TestProject(), new(["required"]), ItemTemplateCompatibility.Incompatible, diagnostics);

        diagnostics.Clear();
        Action changeCaptured = () => ((IList<ItemTemplateCompatibilityDiagnostic>)result.Diagnostics).Clear();

        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("capability-missing");
        changeCaptured.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Result_DefaultCompatibility_IsIncompatibleAndFailsCheck()
    {
        var result = new ItemTemplateCompatibilityResult(new TestProject(), new(["required"]), default, []);

        result.Compatibility.Should().Be(ItemTemplateCompatibility.Incompatible);
        result.PassesProjectCapabilityCheck.Should().BeFalse();
    }

    [Fact]
    public void Result_NullInputs_Throw()
    {
        var project = new TestProject();
        var requirements = new ItemTemplateRequirements(["required"]);
        Action nullProject = () => new ItemTemplateCompatibilityResult(null!, requirements, ItemTemplateCompatibility.Incompatible, []);
        Action nullRequirements = () => new ItemTemplateCompatibilityResult(project, null!, ItemTemplateCompatibility.Incompatible, []);
        Action nullDiagnostics = () => new ItemTemplateCompatibilityResult(project, requirements, ItemTemplateCompatibility.Incompatible, null!);

        nullProject.Should().Throw<ArgumentNullException>().WithParameterName("project");
        nullRequirements.Should().Throw<ArgumentNullException>().WithParameterName("requirements");
        nullDiagnostics.Should().Throw<ArgumentNullException>().WithParameterName("diagnostics");
    }

    private sealed class TestProject : FunctionsProject
    {
        public TestProject()
        {
        }

        public TestProject(IEnumerable<ProjectCapability> capabilities)
            : base(capabilities)
        {
        }

        public override WorkingDirectory WorkingDirectory => throw new InvalidOperationException("Matching must not inspect the project directory.");

        public override string StackName => throw new InvalidOperationException("Matching must not interpret stack identifiers.");

        public override string StackDisplayName => throw new InvalidOperationException("Matching must not read display names.");

        public override string? Language => throw new InvalidOperationException("Matching must not read language labels.");

        public override bool SupportsExtensionBundles => throw new InvalidOperationException("Matching must not read bundle policy.");

        public override FunctionsWorkerReference WorkerReference => throw new InvalidOperationException("Matching must not read worker references.");
    }
}
