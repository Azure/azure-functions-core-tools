// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateEffectWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-effects-" + Guid.NewGuid().ToString("N"));
    private readonly NativeTemplateEffectPlanner _planner = new();
    private readonly NativeTemplateEffectWorkflow _workflow = new();

    private WorkingDirectory Output => WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
    private string Project => Path.Combine(Output.Info.FullName, "Selected.csproj");
    private TemplateEffectSource Source => new("Controlled.Effects", "owned/source", new string('a', 64));

    [Fact]
    public async Task Planner_ActualNativeEvaluatedConditions_PreservesActionOrderExactPrereleaseAndNoOp()
    {
        using var session = await OpenNativeAsync();
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);
        var native = await creator.InstantiateAsync(template, "Named", null, Output.Info.FullName,
            new Dictionary<string, string?> { ["IncludeReference"] = "true" }, forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);
        native.Status.Should().Be(CreationResultStatus.Success);

        var result = _planner.PlanDependencies(Output, Source, native.CreationEffects!.CreationResult, Project);

        result.Diagnostics.Should().BeEmpty();
        result.Plan!.Effects.Should().HaveCount(3);
        result.Plan.Effects[0].Should().BeOfType<TemplatePackageReferenceEffect>().Which.Should()
            .Be(new TemplatePackageReferenceEffect(Source, Project, "Controlled.Package", "2.0.0-preview.3", ".csproj", true));
        result.Plan.Effects[1].Should().BeOfType<TemplateRestoreEffect>();
        result.Plan.Effects[2].Should().BeOfType<TemplateEditorNoOpEffect>();
        Directory.Exists(Output.Info.FullName).Should().BeFalse();

        var excluded = await creator.InstantiateAsync(template, "Named", null, Output.Info.FullName,
            new Dictionary<string, string?> { ["IncludeReference"] = "false" }, forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);
        var withoutReference = _planner.PlanDependencies(Output, Source, excluded.CreationEffects!.CreationResult, Project);
        withoutReference.Plan!.Effects.Should().HaveCount(2).And.NotContain(effect => effect is TemplatePackageReferenceEffect);
    }

    [Fact]
    public async Task Planner_UnknownNativeActionEvenContinueOnError_RejectsWholePlanBeforeCapability()
    {
        using var session = await OpenNativeAsync(unknownAction: true);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);
        var native = await creator.InstantiateAsync(template, "Named", null, Output.Info.FullName,
            new Dictionary<string, string?>(), forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);

        var result = _planner.PlanDependencies(Output, Source, native.CreationEffects!.CreationResult, Project);

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("not handled", StringComparison.Ordinal));
        Directory.Exists(Output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task DefaultNativeItem_StillRejectsPostActionsBeforeAnyMutation()
    {
        using var session = await OpenNativeAsync();
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(Source.TemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(Output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("qualified dispatcher");
        Directory.Exists(Output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task NativeSession_ExplicitCapability_PreviewsThenRequiresConfirmationBeforeFilesOrActions()
    {
        using var session = await OpenNativeAsync();
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(Source.TemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var capability = Capability();
        var support = new NativeTemplateInvocationEffects(Output, Project, capability, Confirmed: false);
        var request = new TemplateInvocationRequest(Output, new Dictionary<string, string?>(), DryRun: true) { EffectSupport = support };

        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        var declined = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Effects!.Kind.Should().Be("previewed");
        preview.Effects.Plan.Effects.Should().HaveCount(3);
        preview.Effects.Plan.Effects[0].Source.TemplateIdentity.Should().Be(selected.Entry.Identity);
        preview.Effects.Plan.Effects[0].Source.Identity.Should().Contain("::");
        declined.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        declined.Effects!.Kind.Should().Be("declined");
        Directory.Exists(Output.Info.FullName).Should().BeFalse();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());

        var created = await selected.InvokeAsync(request with { DryRun = false, EffectSupport = support with { Confirmed = true } }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Path.Combine(Output.Info.FullName, "source.txt")).Should().Be("native source");
        created.Effects!.Kind.Should().Be("applied");
        created.Effects.CompletedEffects.Should().HaveCount(3);
        await capability.Received(2).ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEditorNoOpEffect>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NativeSession_UnknownAction_RejectsBeforeCapabilityOrNativeFileMutation()
    {
        using var session = await OpenNativeAsync(unknownAction: true);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(Source.TemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var capability = Capability();

        var result = await selected.InvokeAsync(new(Output, new Dictionary<string, string?>())
        {
            EffectSupport = new(Output, Project, capability, Confirmed: true),
        }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().Contain("not handled");
        capability.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(Output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task NativeSession_FailedEffect_ReportsRetainedNativeFilesAndStructuredPartialOutcome()
    {
        using var session = await OpenNativeAsync();
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(Source.TemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var capability = Capability();
        capability.ExecuteAsync(Arg.Any<TemplatePackageReferenceEffect>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(false, ["Expected package failure."], TargetMayHaveChanged: true));

        var result = await selected.InvokeAsync(new(Output, new Dictionary<string, string?>())
        {
            EffectSupport = new(Output, Project, capability, Confirmed: true),
        }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Changes.Should().Contain(change => Path.GetFullPath(Path.Combine(Output.Info.FullName, change.Path))
            == Path.Combine(Output.Info.FullName, "source.txt"));
        File.ReadAllText(Path.Combine(Output.Info.FullName, "source.txt")).Should().Be("native source");
        result.Diagnostic.Should().Contain("files were generated").And.Contain("Expected package failure.");
        result.Effects!.Succeeded.Should().BeFalse();
        result.Effects.CompletedEffects.Should().HaveCount(2);
        result.Effects.FailedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplatePackageReferenceEffect>();
        TestInteractionService interaction = new();

        int exit = new NewExecutionRenderer(interaction).Render(new("invocationFailed", false)
        {
            Invocation = result,
            SelectedTemplate = selected.Entry,
            Diagnostics = [result.Diagnostic!],
        }, jsonOutput: true);

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("\"kind\":\"partialFailure\"").And.Contain("\"failed\":").And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task NativeSession_SelectedConfigurationChangesDuringPreflight_UsesExistingFingerprintGuard()
    {
        using var session = await OpenNativeAsync();
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(Source.TemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var capability = Capability();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.AppendAllText(Path.Combine(_root, "package", ".template.config", "template.json"), Environment.NewLine);
            return Task.FromResult<IReadOnlyList<string>>([]);
        });

        var result = await selected.InvokeAsync(new(Output, new Dictionary<string, string?>())
        {
            EffectSupport = new(Output, Project, capability, Confirmed: true),
        }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(Output.Info.FullName).Should().BeFalse();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, false, "previewed", true)]
    [InlineData(false, false, "declined", false)]
    public async Task Workflow_PreviewAndDecline_NeverExecute(bool dryRun, bool confirmed, string kind, bool succeeded)
    {
        var capability = Capability();
        var plan = Plan(new TemplateAppendFileEffect(Source, Path.Combine(Output.Info.FullName, "function_app.py"), "snippet", new string('b', 64)));

        var result = await _workflow.ExecuteAsync(plan, capability, dryRun, confirmed, CancellationToken.None);

        result.Kind.Should().Be(kind);
        result.Succeeded.Should().Be(succeeded);
        result.Plan.Effects.Should().ContainSingle();
        result.CompletedEffects.Should().BeEmpty();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_AbsentCapability_DefaultDenies()
    {
        var result = await _workflow.ExecuteAsync(Plan(new TemplateRestoreEffect(Source, Project, false)),
            null, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("capabilityRequired");
        result.Succeeded.Should().BeFalse();
        result.CompletedEffects.Should().BeEmpty();
    }

    [Fact]
    public async Task Workflow_ChangedInputDetectedByPreflight_RejectsBeforeFirstEffect()
    {
        var capability = Capability();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(["Source or target changed."]);
        var plan = Plan(new TemplateRestoreEffect(Source, Project, false));

        var result = await _workflow.ExecuteAsync(plan, capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("preflightFailed");
        result.Diagnostics.Should().Contain("Source or target changed.");
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_FailedContinuableAction_RetainsFailureAndRunsNextWithoutSuccessShapedFallback()
    {
        var capability = Capability();
        var package = new TemplatePackageReferenceEffect(Source, Project, "Controlled.Package", "1.0.0", ".csproj", true);
        var restore = new TemplateRestoreEffect(Source, Project, false);
        capability.ExecuteAsync(package, Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(false, ["Package add failed."], TargetMayHaveChanged: true));
        var plan = Plan(package, restore);

        var result = await _workflow.ExecuteAsync(plan, capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("partialFailure");
        result.Succeeded.Should().BeFalse();
        result.FailedEffects.Should().Equal(package);
        result.CompletedEffects.Should().Equal(restore);
        result.Diagnostics.Should().Contain("Package add failed.").And.Contain(diagnostic => diagnostic.Contains("no rollback", StringComparison.Ordinal));
        await capability.Received(1).ExecuteAsync(restore, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_FailedRequiredEffect_StopsAndRetainsCompletedEffects()
    {
        var capability = Capability();
        var first = new TemplateRestoreEffect(Source, Project, false);
        var failed = new TemplateAppendFileEffect(Source, Path.Combine(Output.Info.FullName, "function_app.py"), "snippet", new string('b', 64));
        var unstarted = new TemplateCreateFileEffect(Source, Path.Combine(Output.Info.FullName, "requirements.psd1"), "requirements");
        capability.ExecuteAsync(failed, Arg.Any<CancellationToken>()).Returns(new TemplateEffectExecutionResult(false, ["Target changed."]));

        var result = await _workflow.ExecuteAsync(Plan(first, failed, unstarted), capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.CompletedEffects.Should().Equal(first);
        result.FailedEffects.Should().Equal(failed);
        await capability.DidNotReceive().ExecuteAsync(unstarted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_CancellationAfterPreflight_DoesNotExecuteOrReturnSuccess()
    {
        using CancellationTokenSource cancellation = new();
        var capability = Capability();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<string>>([]);
        });

        Func<Task> act = () => _workflow.ExecuteAsync(Plan(new TemplateRestoreEffect(Source, Project, false)),
            capability, dryRun: false, confirmed: true, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_UnexpectedProviderFailure_PropagatesUnwrapped()
    {
        var capability = Capability();
        var failure = new InvalidOperationException("Provider bug");
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns<Task<TemplateEffectExecutionResult>>(_ => throw failure);

        Func<Task> act = () => _workflow.ExecuteAsync(Plan(new TemplateRestoreEffect(Source, Project, false)),
            capability, dryRun: false, confirmed: true, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task Workflow_UnsupportedEffectAfterValidOne_DeniesWholePlanBeforeCapability()
    {
        var capability = Capability();
        var plan = new NativeTemplateEffectPlan(Output, [new TemplateRestoreEffect(Source, Project, false),
            new UnknownEffect(Source, Project)]);

        var result = await _workflow.ExecuteAsync(plan, capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("invalidPlan");
        capability.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Planner_CreateOnlyQualificationAndBoundary_DoNotBroadenNativeProjectGuards()
    {
        var allowed = _planner.PlanFilesAndDependencies(Output,
            [new TemplateCreateFileEffect(Source, Path.Combine(Output.Info.FullName, "requirements.psd1"), "expanded")]);
        var other = _planner.PlanFilesAndDependencies(Output,
            [new TemplateCreateFileEffect(Source, Path.Combine(Output.Info.FullName, "other.txt"), "expanded")]);
        var outside = _planner.PlanFilesAndDependencies(Output,
            [new TemplateAppendFileEffect(Source, Path.Combine(_root, "outside.py"), "expanded", new string('b', 64))]);

        allowed.Plan.Should().NotBeNull();
        other.Plan.Should().BeNull();
        outside.Plan.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private NativeTemplateEffectPlan Plan(params TemplateEffect[] effects)
    {
        var result = _planner.PlanFilesAndDependencies(Output, effects);
        result.Diagnostics.Should().BeEmpty();
        return result.Plan!;
    }

    private static INativeTemplateEffectCapability Capability()
    {
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns(new TemplateEffectExecutionResult(true, []));
        return capability;
    }

    private async Task<TemplateEngineSession> OpenNativeAsync(bool unknownAction = false)
    {
        string package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        File.WriteAllText(Path.Combine(package, "source.txt"), "native source");
        string unknown = unknownAction
            ? """,{"actionId":"f3ba64f5-68a9-4ac4-9cfe-4519e4940a77","continueOnError":true,"args":{"executable":"unhandled"}}"""
            : string.Empty;
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), $$$"""
            {"identity":"Controlled.Effects","name":"Controlled actions","shortName":"controlled-effects",
             "tags":{"type":"item"},"symbols":{"IncludeReference":{"type":"parameter","datatype":"bool","defaultValue":"true"}},
             "postActions":[
               {"condition":"(IncludeReference)","actionId":"{{{NativeTemplateEffectPlanner.PackageReferenceActionId}}}",
                "continueOnError":true,"args":{"referenceType":"package","reference":"Controlled.Package",
                  "version":"2.0.0-preview.3","projectFileExtensions":".csproj"}},
               {"actionId":"{{{NativeTemplateEffectPlanner.RestoreActionId}}}","args":{}},
               {"actionId":"{{{NativeTemplateEffectPlanner.EditorActionId}}}","continueOnError":true,"args":{"files":"0"}}
               {{{unknown}}}]}
            """);
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive, CancellationToken.None);
        return new(new(WorkingDirectory.FromExplicit(_root)), hive);
    }

    private sealed record UnknownEffect(TemplateEffectSource Source, string Path) : TemplateEffect(Source, Path);
}
