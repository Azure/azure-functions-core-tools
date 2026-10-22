// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Templates.V2;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("HttpTrigger-Python")]
    [InlineData("EventGridTrigger-Python")]
    [InlineData("ServiceBusTopicTrigger-Python")]
    [InlineData("DurableFunctionsEntityTrigger-Python")]
    [InlineData("DurableFunctionsOrchestration-Python")]
    public async Task SelectedAppend_RealPackagedMapping_PreviewsThenAppendsThroughOwnedExecutorWithoutReplacingUserCode(string id)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "import json\nimport logging\nimport azure.functions as func\napp = func.FunctionApp()\n@app.route(route='existing')\ndef existing(req):\n    return func.HttpResponse('user code')";
        File.WriteAllText(app, original);
        if (mapping.CreateTargetFileName != "function_app.py")
        {
            File.WriteAllText(Path.Combine(output.Info.FullName, mapping.CreateTargetFileName), "legacy create marker");
        }

        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app);
        var support = AppendSupport(mapping, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);
        var parameters = selected.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue, StringComparer.Ordinal);
        var request = new TemplateInvocationRequest(output, parameters, "Greeting", DryRun: true);

        var preview = await support.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Kind.Should().Be("previewed");
        preview.Changes.Should().BeEmpty();
        preview.HasCommittedOutput.Should().BeFalse();
        var plannedAppend = preview.Effects.Plan.Effects.Should().ContainSingle().Subject
            .Should().BeOfType<TemplateAppendFileEffect>().Subject;
        plannedAppend.TargetPath.Should().Be(app);
        plannedAppend.Source.Should().Be(mapping.HelperSource);
        preview.Effects.CompletedEffects.Should().BeEmpty();
        File.ReadAllText(app).Should().Be(original);
        capability.Executions.Should().Be(0);

        var created = await support.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Effects!.Kind.Should().Be("applied");
        created.Changes.Should().BeEmpty();
        created.HasCommittedOutput.Should().BeFalse();
        created.Effects.CompletedEffects.Should().ContainSingle().Which
            .Should().BeOfType<TemplateAppendFileEffect>().Which.Should().Be(plannedAppend);
        capability.Executions.Should().Be(1);
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy"));
        File.WriteAllText(Path.Combine(legacy.FullName, "function_app.py"), original);
        if (_authorizedFilenameCorrections.Contains(id))
        {
            File.WriteAllText(Path.Combine(legacy.FullName, "function_app.py.py"), "legacy create marker");
        }

        parameters["trigger-functionName"] = "Greeting";
        new V2TemplateEngine().Apply(_sources.Single(source => source.Id == id), "Greeting", parameters, legacy, false)
            .Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Path.Combine(legacy.FullName, "function_app.py")));
        File.ReadAllText(app).Should().StartWith(original);
        Directory.GetFiles(output.Info.FullName).Should().HaveCount(mapping.CreateTargetFileName == "function_app.py" ? 1 : 2);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("createSource")]
    [InlineData("helperSource")]
    [InlineData("configuration")]
    [InlineData("helperConfiguration")]
    [InlineData("packageVersion")]
    [InlineData("helperIdentity")]
    [InlineData("expectedOutput")]
    public async Task SelectedAppend_SourceBoundMismatch_RefusesBeforeOwnedExecutorAndPreservesApp(string mismatch)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code");
        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app);
        var invalid = mismatch switch
        {
            "mapping" => mapping with { MappingSource = mapping.MappingSource with { Sha256 = new string('0', 64) } },
            "createSource" => mapping with { CreateSource = mapping.CreateSource with { Sha256 = new string('0', 64) } },
            "helperSource" => mapping with { HelperSource = mapping.HelperSource with { Sha256 = new string('0', 64) } },
            "configuration" => mapping with { ConfigurationSha256 = new string('0', 64) },
            "helperConfiguration" => mapping with { HelperConfigurationSha256 = new string('0', 64) },
            "packageVersion" => mapping with { Package = mapping.Package with { Version = "99.0.0" } },
            "helperIdentity" => mapping with { HelperIdentity = "Not.The.Mapped.Helper" },
            "expectedOutput" => mapping with { ExpectedHelperOutput = "not-the-expected-file.py" },
            _ => throw new InvalidOperationException("Unknown negative fixture."),
        };
        var support = AppendSupport(invalid, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding, new(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "Greeting"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().NotBeNullOrWhiteSpace();
        File.ReadAllText(app).Should().Be("user code");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("changed")]
    [InlineData("canceled")]
    public async Task SelectedAppend_DeclineChangeOrCancelAfterRealExpansion_DoesNotAppend(string state)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code");
        using CancellationTokenSource cancellation = new();
        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app)
        {
            BeforeValidation = state == "canceled" ? cancellation.Cancel : null,
        };
        var support = AppendSupport(mapping, capability, confirmed: state != "declined",
            onHelper: state == "changed" ? () => File.WriteAllText(app, "new user edits") : null);
        var binding = await support.ResolveAsync(selected, cancellation.Token);
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "Greeting");
        if (state == "canceled")
        {
            Func<Task> invoke = () => support.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await support.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
            result.Effects!.Kind.Should().Be(state == "declined" ? "declined" : "preflightFailed");
        }

        capability.Executions.Should().Be(0);
        File.ReadAllText(app).Should().Be(state == "changed" ? "new user edits" : "user code");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData("HttpTrigger-Python")]
    [InlineData("EventGridTrigger-Python")]
    public async Task SelectedAppend_MappedNativeCreateTargetAbsent_UsesOrdinaryNativeCreateWithoutEffects(string id)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "create"));
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app);
        var support = AppendSupport(mapping, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);
        var request = new TemplateInvocationRequest(output,
            selected.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue), "Greeting");

        var effect = await support.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        effect.Should().BeNull("the exact opted-in native create target is absent");
        capability.Executions.Should().Be(0);
        var created = await selected.InvokeAsync(request, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.Exists(Path.Combine(output.Info.FullName, mapping.CreateTargetFileName)).Should().BeTrue();
        File.Exists(app).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
    }

    [Fact]
    public async Task SelectedAppend_MissingEffectiveName_RefusesDespiteAuthoredDefaultWithoutLiveEffects()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code");
        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app);
        var support = AppendSupport(mapping, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding, new(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }), CancellationToken.None);

        selected.Entry.DefaultName.Should().Be("http_trigger");
        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().Contain("effective name");
        File.ReadAllText(app).Should().Be("user code");
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRunner_RealPythonHelpersAreDeniedRegardlessOfCapability_OriginalHiddenDurableRemainsApplicable(bool includeCapability)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var helperIdentity = Applications.EnumerateArray()
            .Single(item => item.GetProperty("templateIdentity").GetString() == "Azure.Functions.Python.HttpTrigger")
            .GetProperty("appendHelper").GetProperty("templateIdentity").GetString()!;
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "ordinary"));
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        NativeSelectedItemAppendSupport support = new(new PhysicalFileSystem(), capability,
            (_, _) => throw new InvalidOperationException("Ordinary helper rejection must precede mapping resolution."), confirmed: true);
        var interaction = Substitute.For<IInteractionService>();
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(templater.Context, null));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
        TemplateArgumentParser parser = new();
        NewExecutionRunner runner = new(contexts, factory, parser, interaction, new(parser, interaction));
        var request = new NewExecutionRequest(output, helperIdentity, "Greeting", null, false, true, false, false, false,
            [], new HashSet<string>()) { SelectedItemEffects = includeCapability ? support : null };

        var result = await runner.ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be("internalTemplate");
        result.Succeeded.Should().BeFalse();
        capability.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        using var second = CreateTemplater(await ReadBundleAsync("stable"));
        var original = (await second.ResolveGroupAsync("DurableFunctionsEntityTrigger-Python", TemplateType.Item)).Group!.Single();
        original.Entry.IsHidden.Should().BeTrue();
        original.Entry.DeclaredInternalRole.Should().BeNull();
        var created = await original.InvokeAsync(new(output, new Dictionary<string, string?>(), "Greeting"), CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
    }

    private async Task<NativeItemAppendMapping> ReadAppendMappingAsync(ResolvedTemplate selected, CancellationToken cancellationToken = default)
    {
        var source = await new PythonTemplateSourceReader().ReadAsync(selected, cancellationToken);
        source.Binding.Diagnostics.Should().BeEmpty();
        return source.Binding.Append!;
    }

    [Theory]
    [InlineData("HttpTrigger-Python", null, false, true)]
    [InlineData("HttpTrigger-Python", "bad-name", false, false)]
    [InlineData("TimerTrigger-Python", "Greeting", true, false)]
    public async Task Compatibility_ActualPackagedPromptSource_PreservesAuthoredDefaultAndRegexRefusals(
        string id, string? requestedName, bool invalidSchedule, bool expectedSuccess)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var interaction = Substitute.For<IInteractionService>();
        PythonItemCompatibility compatibility = new(interaction, new PythonTemplateSourceReader());
        string[] tokens = invalidSchedule ? ["--timer-trigger-schedule", "not-a-cron"] : [];
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), tokens));

        var result = await compatibility.ResolveAsync(selected, arguments, requestedName, false, CancellationToken.None);

        result.Succeeded.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            result.Name.Should().Be("http_trigger");
            result.Origin.Should().Be("authoredDefault");
        }
        else
        {
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("original pattern", StringComparison.Ordinal));
        }

        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    private NativeSelectedItemAppendSupport AppendSupport(NativeItemAppendMapping mapping, INativeTemplateEffectCapability capability,
        bool confirmed = true, Action? onHelper = null) => new(new PhysicalFileSystem(), capability, (selected, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool helper = _helperIdentities.Contains(selected.Entry.Identity);
        if (helper)
        {
            onHelper?.Invoke();
        }

        return Task.FromResult(new NativeSelectedItemBinding(helper,
            selected.Entry.Identity == mapping.CreateSource.TemplateIdentity ? mapping : null, []));
    }, confirmed);

    private sealed class OwnedPythonAppendCapability(ResolvedTemplate selected, TemplateEffectSource source, string target)
        : INativeTemplateEffectCapability
    {
        private readonly PythonAppendCapability _inner = new(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor(), selected,
            WorkingDirectory.FromExplicit(Path.GetDirectoryName(target)!));
        private readonly TemplateEffectSource _source = source;

        public int Executions { get; private set; }

        public Action? BeforeValidation { get; init; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            BeforeValidation?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Effects.Any(effect => effect.Source != _source))
            {
                return ["The test supplied a different approved source."];
            }
            return await _inner.ValidateAsync(plan, cancellationToken);
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            Executions++;
            return await _inner.ExecuteAsync(effect, cancellationToken);
        }
    }
}
