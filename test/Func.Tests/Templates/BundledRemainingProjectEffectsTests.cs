// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.PowerShell;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "12")]
    [InlineData(true, null)]
    public async Task PowerShellPreparation_InitCallback_CompletesOwnedConditionalEffectsAndRetainsLookupDiagnostics(
        bool enabled, string? major)
    {
        using var services = await InstallAsync("PowerShell");
        ControlledPowerShellPreparation owned = new(major);
        OwnedPowerShellPreparationCallback callback = new(owned);
        var request = PowerShellPreparationRequest(callback, enabled);

        var result = await PreparationRunner(services).ExecuteAsync(request, Snapshot("powershell"), CancellationToken.None);

        result.Kind.Should().Be("created");
        result.Succeeded.Should().BeTrue();
        result.Preparation!.Preview.FileEffects!.Effects.Should().HaveCount(enabled ? 1 : 0);
        result.Preparation.Completion!.Effects!.Kind.Should().Be("applied");
        result.Preparation.Completion.Effects.CompletedEffects.Should().HaveCount(enabled ? 1 : 0);
        owned.Lookups.Should().Be(enabled ? 1 : 0);
        string target = Path.Combine(request.WorkingDirectory.Info.FullName, "requirements.psd1");
        File.Exists(target).Should().Be(enabled);
        if (enabled)
        {
            string source = new PowerShellProjectPreparation().ReadRequirementsTemplate();
            File.ReadAllText(target).Should().Be(major is null ? source : source.Replace("MAJOR_VERSION", major, StringComparison.Ordinal));
        }
        if (enabled && major is null)
        {
            result.Preparation.Preview.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented");
            result.Preparation.Completion.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented");
        }
        TestInteractionService rendered = new();
        new InitExecutionRenderer(rendered).Render(result, jsonOutput: false).Should().Be(0);
        rendered.AllOutput.Should().Contain(enabled ? "PowerShellManagedDependencies: Completed" : "PowerShellManagedDependencies: Skipped");
        if (enabled && major is null) rendered.AllOutput.Should().Contain("Could not resolve", "MAJOR_VERSION");
    }

    [Fact]
    public async Task PowerShellPreparation_InitCallback_DryRunIncludesOwnedFilePlanWithoutLiveOutput()
    {
        using var services = await InstallAsync("PowerShell");
        ControlledPowerShellPreparation owned = new("12");
        OwnedPowerShellPreparationCallback callback = new(owned);
        var request = PowerShellPreparationRequest(callback, enabled: true) with { DryRun = true };

        var result = await PreparationRunner(services).ExecuteAsync(request, Snapshot("powershell"), CancellationToken.None);

        result.Kind.Should().Be("previewed");
        result.Preparation!.Preview.FileEffects!.Effects.Should().ContainSingle().Which.Should().BeOfType<TemplateCreateFileEffect>();
        result.Preparation.Completion.Should().BeNull();
        callback.Capability!.Executions.Should().Be(0);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task PowerShellPreparation_InitCallback_LateOccupiedRequirements_ReportsPartialPreparationWithoutReplacingUserBytes()
    {
        using var services = await InstallAsync("PowerShell");
        ControlledPowerShellPreparation owned = new("12");
        OwnedPowerShellPreparationCallback callback = new(owned);
        var request = PowerShellPreparationRequest(callback, enabled: true);
        string target = Path.Combine(request.WorkingDirectory.Info.FullName, "requirements.psd1");
        callback.BeforeCompletion = () => File.WriteAllText(target, "user requirements appeared after native creation");

        var result = await PreparationRunner(services).ExecuteAsync(request, Snapshot("powershell"), CancellationToken.None);

        result.Kind.Should().Be("partialPreparation");
        result.Succeeded.Should().BeFalse();
        result.Preparation!.Completion!.Effects!.Kind.Should().Be("preflightFailed");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("occupied", StringComparison.Ordinal));
        File.ReadAllText(target).Should().Be("user requirements appeared after native creation");
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
        TestInteractionService rendered = new();
        new InitExecutionRenderer(rendered).Render(result, jsonOutput: false).Should().Be(1);
        rendered.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Fact]
    public async Task PowerShellEffect_DefaultFalse_CreatesNativeProjectWithoutLookupOrRequirements()
    {
        using var journey = await OpenPowerShellEffectJourneyAsync([]);

        var nativePreview = await journey.Selected.InvokeAsync(journey.Request with { DryRun = true }, CancellationToken.None);
        var effectPreview = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: true, confirmed: false, CancellationToken.None);

        nativePreview.Status.Should().Be(TemplateInvocationStatus.Success, nativePreview.Diagnostic);
        effectPreview.Kind.Should().Be("previewed");
        journey.Plan.Effects.Should().BeEmpty();
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();

        await journey.CreateBaseAsync(CancellationToken.None);
        var result = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.CompletedEffects.Should().BeEmpty();
        journey.Preparation.Lookups.Should().Be(0);
        journey.Preparation.SourceReads.Should().Be(0);
        journey.Capability.SourceReads.Should().Be(0);
        File.Exists(journey.Target).Should().BeFalse();
        JsonNode.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "host.json")))!
            .AsObject().Should().NotContainKey("managedDependency");
    }

    [Theory]
    [InlineData("7.4", "12")]
    [InlineData("7.6", "14")]
    [InlineData("7.4", null)]
    public async Task PowerShellEffect_ManagedTrue_PreviewsThenCreatesExactOwnedRequirementsWithExplicitLookupOutcome(
        string runtime, string? major)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(
            ["--managed-dependencies", "true", "--runtime-version", runtime], major);
        string original = new PowerShellProjectPreparation().ReadRequirementsTemplate();
        string expected = major is null ? original : original.Replace("MAJOR_VERSION", major, StringComparison.Ordinal);

        var nativePreview = await journey.Selected.InvokeAsync(journey.Request with { DryRun = true }, CancellationToken.None);
        var preview = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: true, confirmed: true, CancellationToken.None);

        nativePreview.Status.Should().Be(TemplateInvocationStatus.Success, nativePreview.Diagnostic);
        nativePreview.Effects.Should().BeNull("this is a test-owned bridge, not a native project-session hook");
        nativePreview.Changes.Should().NotContain(change => change.Path.EndsWith("requirements.psd1", StringComparison.Ordinal));
        preview.Kind.Should().Be("previewed");
        preview.Plan.Effects.Should().ContainSingle().Which.Should().BeOfType<TemplateCreateFileEffect>()
            .Which.Content.Should().Be(expected);
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        journey.Capability.Executions.Should().Be(0);

        await journey.CreateBaseAsync(CancellationToken.None);
        string configBefore = File.ReadAllText(Path.Combine(journey.Output.Info.FullName, ".func", "config.json"));
        var created = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        created.Kind.Should().Be("applied");
        created.Succeeded.Should().BeTrue();
        created.CompletedEffects.Should().ContainSingle();
        File.ReadAllText(journey.Target).Should().Be(expected);
        File.ReadAllText(Path.Combine(journey.Output.Info.FullName, ".func", "config.json")).Should().Be(configBefore);
        JsonNode.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "host.json")))!
            ["managedDependency"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        JsonNode.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "local.settings.json")))!
            ["Values"]!["FUNCTIONS_WORKER_RUNTIME_VERSION"]!.GetValue<string>().Should().Be(runtime);
        journey.Preparation.Lookups.Should().Be(1);
        if (major is null)
        {
            journey.Prepared.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented", "MAJOR_VERSION");
            File.ReadAllText(journey.Target).Should().Contain("# 'Az' = 'MAJOR_VERSION.*'");
        }
        else
        {
            journey.Prepared.Diagnostics.Should().BeEmpty();
            File.ReadAllText(journey.Target).Should().Contain($"# 'Az' = '{major}.*'").And.NotContain("MAJOR_VERSION");
        }

        var repeated = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);
        repeated.Kind.Should().Be("preflightFailed");
        journey.Capability.Executions.Should().Be(1);
        File.ReadAllText(journey.Target).Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerShellEffect_MissingCapabilityOrDeclinedPlan_DoesNotCreateLiveOutput(bool absentCapability)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);

        var result = await new NativeTemplateEffectWorkflow().ExecuteAsync(journey.Plan,
            absentCapability ? null : journey.Capability, dryRun: false, confirmed: false, CancellationToken.None);

        result.Kind.Should().Be(absentCapability ? "capabilityRequired" : "declined");
        result.Succeeded.Should().BeFalse();
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        journey.Capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerShellEffect_OccupiedDestination_RefusesFileOrDirectoryWithoutReplacingUserData(bool directory)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);
        await journey.CreateBaseAsync(CancellationToken.None);
        if (directory) Directory.CreateDirectory(journey.Target);
        else File.WriteAllText(journey.Target, "user-owned requirements");
        string before = File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "host.json"));

        var result = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("preflightFailed");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("occupied", StringComparison.Ordinal));
        journey.Capability.Executions.Should().Be(0);
        File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "host.json")).Should().Be(before);
        if (directory) Directory.Exists(journey.Target).Should().BeTrue();
        else File.ReadAllText(journey.Target).Should().Be("user-owned requirements");
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("source")]
    [InlineData("template")]
    [InlineData("content")]
    public async Task PowerShellEffect_UnboundSourceOrContent_RefusesBeforeAnyLiveWrite(string changed)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);
        var effect = (TemplateCreateFileEffect)journey.Plan.Effects.Single();
        effect = changed switch
        {
            "hash" => effect with { Source = effect.Source with { Sha256 = new string('0', 64) } },
            "source" => effect with { Source = effect.Source with { Identity = "another-owned-resource" } },
            "template" => effect with { Source = effect.Source with { TemplateIdentity = "Azure.Functions.Go.Basic" } },
            _ => effect with { Content = "unapproved replacement content" },
        };
        var plan = new NativeTemplateEffectPlanner().PlanFilesAndDependencies(journey.Output, [effect]).Plan!;

        var result = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("preflightFailed");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("bound", StringComparison.Ordinal));
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        journey.Capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerShellEffect_SourceChangedBeforePreflightOrExecution_RefusesPinnedHash(bool duringExecution)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);
        if (duringExecution)
        {
            await journey.CreateBaseAsync(CancellationToken.None);
            journey.Capability.BeforeExecution = () => journey.Capability.CorruptPackageSource = true;
        }
        else
        {
            journey.Capability.CorruptPackageSource = true;
        }

        var result = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be(duringExecution ? "partialFailure" : "preflightFailed");
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("source hash", StringComparison.Ordinal));
        File.Exists(journey.Target).Should().BeFalse();
        if (duringExecution)
        {
            File.Exists(Path.Combine(journey.Output.Info.FullName, ".func", "config.json")).Should().BeTrue();
            result.FailedEffects.Should().ContainSingle();
        }
        else
        {
            Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerShellEffect_CancellationAfterPreflightOrBeforeExecution_DoesNotWriteRequirements(bool duringExecution)
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);
        using CancellationTokenSource cancellation = new();
        if (duringExecution)
        {
            await journey.CreateBaseAsync(cancellation.Token);
            journey.Capability.BeforeExecution = cancellation.Cancel;
        }
        else
        {
            journey.Capability.AfterPreflight = cancellation.Cancel;
        }

        Func<Task> action = () => new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(journey.Target).Should().BeFalse();
        if (!duringExecution) Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task PowerShellEffect_DestinationAppearsAtAtomicCommit_PreservesWinnerAndReportsFailure()
    {
        using var journey = await OpenPowerShellEffectJourneyAsync(["--managed-dependencies", "true"]);
        await journey.CreateBaseAsync(CancellationToken.None);
        journey.Capability.Files = new OccupiedRequirementsFileSystem(journey.Target);

        var result = await new NativeTemplateEffectWorkflow()
            .ExecuteAsync(journey.Plan, journey.Capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("partialFailure");
        result.FailedEffects.Should().ContainSingle();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("create-only write", StringComparison.Ordinal));
        File.ReadAllText(journey.Target).Should().Be("user content won the create race");
        Directory.GetFiles(journey.Output.Info.FullName, "requirements.psd1.tmp.*").Should().BeEmpty();
    }

    private async Task<PowerShellEffectJourney> OpenPowerShellEffectJourneyAsync(
        IReadOnlyList<string> tokens, string? major = "12", CancellationToken cancellationToken = default)
    {
        ServiceProvider services = await InstallAsync("PowerShell", cancellationToken);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "managed-project"));
        Templater templater = ProjectTemplater(services, output, "powershell");
        var resolution = await templater.ResolveGroupAsync("Azure.Functions.PowerShell.Basic", TemplateType.Project, cancellationToken);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success, Diagnostics(resolution));
        ResolvedTemplate selected = resolution.Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters,
            new HashSet<string>(), tokens), cancellationToken);
        arguments.IsComplete.Should().BeTrue();
        bool enabled = bool.Parse(arguments.ValuesByCanonicalName["ManagedDependencies"]!);
        ControlledPowerShellPreparation preparation = new(major);
        PowerShellManagedDependencyContent prepared = await preparation.PrepareManagedDependenciesAsync(enabled, cancellationToken);
        TemplateEffect[] effects = prepared.Content is null ? [] :
            [new TemplateCreateFileEffect(new(selected.Entry.Identity, prepared.SourceIdentity!, prepared.SourceSha256!),
                Path.Combine(output.Info.FullName, "requirements.psd1"), prepared.Content)];
        var plan = new NativeTemplateEffectPlanner().PlanFilesAndDependencies(output, effects);
        plan.Diagnostics.Should().BeEmpty();
        return new(services, templater, selected, new(output, arguments.ValuesByCanonicalName, "ManagedApp"),
            preparation, prepared, plan.Plan!);
    }

    private InitExecutionRequest PowerShellPreparationRequest(INativeProjectPreparation callback, bool enabled)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, "powershell-prepared")), "Azure.Functions.PowerShell.Basic",
            "PowerShellApp", "powershell", "pwsh", Force: false, NonInteractive: true, DryRun: false, JsonOutput: true,
            enabled ? ["--managed-dependencies", "true"] : [], new HashSet<string>())
        {
            ProjectPreparation = callback,
        };

    private sealed class OwnedPowerShellPreparationCallback(ControlledPowerShellPreparation owned) : INativeProjectPreparation
    {
        private PowerShellManagedDependencyContent? _prepared;

        public OwnedRequirementsCapability? Capability { get; private set; }
        public Action? BeforeCompletion { get; set; }

        public async Task<NativeProjectPreparationPreview> PreviewAsync(
            NativeProjectPreparationContext context, CancellationToken cancellationToken)
        {
            context.SelectedTemplate.Entry.Identity.Should().Be("Azure.Functions.PowerShell.Basic");
            context.NativePreview.ProjectConfigurations.Should().ContainSingle().Which.Root.Should().Be(context.TargetDirectory.Info.FullName);
            bool enabled = bool.Parse(context.Parameters["ManagedDependencies"]!);
            _prepared = await owned.PrepareManagedDependenciesAsync(enabled, cancellationToken);
            Capability = new(context.SelectedTemplate, owned, _prepared, context.TargetDirectory);
            TemplateEffect[] effects = _prepared.Content is null ? [] :
                [new TemplateCreateFileEffect(new(context.SelectedTemplate.Entry.Identity, _prepared.SourceIdentity!, _prepared.SourceSha256!),
                    Path.Combine(context.TargetDirectory.Info.FullName, "requirements.psd1"), _prepared.Content)];
            var planned = new NativeTemplateEffectPlanner().PlanFilesAndDependencies(context.TargetDirectory, effects);
            planned.Diagnostics.Should().BeEmpty();
            var preview = await new NativeTemplateEffectWorkflow()
                .ExecuteAsync(planned.Plan!, Capability, dryRun: true, confirmed: true, cancellationToken);
            return new(preview.Succeeded, [new(NativeProjectPreparationOperation.PowerShellManagedDependencies, !enabled)],
                [.. _prepared.Diagnostics, .. preview.Diagnostics], planned.Plan);
        }

        public async Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
            NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
        {
            File.Exists(Path.Combine(context.TargetDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(context.TargetDirectory.Info.FullName, "profile.ps1")).Should().BeTrue();
            BeforeCompletion?.Invoke();
            var result = await new NativeTemplateEffectWorkflow()
                .ExecuteAsync(preview.FileEffects!, Capability, dryRun: false, confirmed: true, cancellationToken);
            return new(result.Succeeded, [.. _prepared!.Diagnostics, .. result.Diagnostics], result);
        }
    }

    // This bridge composes two staged APIs in tests; it deliberately does not register a native project-session hook.
    private sealed class PowerShellEffectJourney(
        ServiceProvider services, Templater templater, ResolvedTemplate selected, TemplateInvocationRequest request,
        ControlledPowerShellPreparation preparation, PowerShellManagedDependencyContent prepared, NativeTemplateEffectPlan plan) : IDisposable
    {
        public ResolvedTemplate Selected { get; } = selected;
        public TemplateInvocationRequest Request { get; } = request;
        public WorkingDirectory Output => Request.OutputDirectory;
        public string Target => Path.Combine(Output.Info.FullName, "requirements.psd1");
        public ControlledPowerShellPreparation Preparation { get; } = preparation;
        public PowerShellManagedDependencyContent Prepared { get; } = prepared;
        public NativeTemplateEffectPlan Plan { get; } = plan;
        public OwnedRequirementsCapability Capability { get; } = new(selected, preparation, prepared, request.OutputDirectory);

        public async Task CreateBaseAsync(CancellationToken cancellationToken)
        {
            var result = await Selected.InvokeAsync(Request, cancellationToken);
            result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
            result.Effects.Should().BeNull();
            File.Exists(Target).Should().BeFalse("the base native template must not create the conditional requirements file");
        }

        public void Dispose()
        {
            templater.Dispose();
            services.Dispose();
        }
    }

    private sealed class ControlledPowerShellPreparation(string? major) : PowerShellProjectPreparation
    {
        public int Lookups { get; private set; }
        public int SourceReads { get; private set; }

        public override Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            return Task.FromResult(major);
        }

        public override string ReadRequirementsTemplate()
        {
            SourceReads++;
            return base.ReadRequirementsTemplate();
        }
    }

    private sealed class OwnedRequirementsCapability(ResolvedTemplate selected, PowerShellProjectPreparation preparation,
        PowerShellManagedDependencyContent prepared, WorkingDirectory output) : INativeTemplateEffectCapability
    {
        public IFileSystem Files { get; set; } = new PhysicalFileSystem();
        public int SourceReads { get; private set; }
        public int Executions { get; private set; }
        public bool CorruptPackageSource { get; set; }
        public Action? AfterPreflight { get; set; }
        public Action? BeforeExecution { get; set; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.OutputDirectory.Info.FullName != output.Info.FullName || plan.Effects.Count > 1)
            {
                return ["The test-owned effect requires its exact project boundary and conditional single-file plan."];
            }
            foreach (TemplateEffect effect in plan.Effects)
            {
                IReadOnlyList<string> diagnostics = await ValidateEffectAsync(effect, cancellationToken);
                if (diagnostics.Count > 0) return diagnostics;
            }
            AfterPreflight?.Invoke();
            return [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            Executions++;
            BeforeExecution?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> diagnostics = await ValidateEffectAsync(effect, cancellationToken);
            if (diagnostics.Count > 0) return new(false, diagnostics);
            var create = (TemplateCreateFileEffect)effect;
            try
            {
                await Files.CreateAllTextAsync(create.TargetPath, create.Content, cancellationToken);
            }
            catch (IOException exception)
            {
                return new(false, [$"The requirements.psd1 create-only write failed: {exception.Message}"]);
            }
            return new(true, []);
        }

        private async Task<IReadOnlyList<string>> ValidateEffectAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (effect is not TemplateCreateFileEffect create || selected.Entry.Identity != "Azure.Functions.PowerShell.Basic"
                || create.Source.TemplateIdentity != selected.Entry.Identity
                || create.Source.Identity != PowerShellProjectPreparation.RequirementsSourceIdentity
                || create.Source.Sha256 != prepared.SourceSha256 || create.Content != prepared.Content
                || create.TargetPath != Path.Combine(output.Info.FullName, "requirements.psd1"))
            {
                return ["The conditional create effect is not bound to its selected PowerShell template, owned source and prepared content."];
            }
            TemplateInvocationResult? configuration = await selected.ValidateConfigurationAsync(cancellationToken);
            if (configuration is not null) return ["The selected PowerShell template configuration changed."];
            ReadOnlyMemory<byte> source = await selected.ReadPackageFileAsync(
                "content/project/.template.config/requirements.psd1", cancellationToken);
            SourceReads++;
            if (CorruptPackageSource) source = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.Span) + "\nchanged source");
            string packagedHash = Convert.ToHexStringLower(SHA256.HashData(source.Span));
            string ownedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preparation.ReadRequirementsTemplate())));
            if (packagedHash != prepared.SourceSha256 || ownedHash != prepared.SourceSha256)
            {
                return ["The packaged or owned requirements source hash changed; no requirements file was written."];
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Files.FileExists(create.TargetPath) || Files.DirectoryExists(create.TargetPath)
                ? ["The requirements.psd1 destination is occupied; existing user content was preserved."] : [];
        }
    }

    private sealed class OccupiedRequirementsFileSystem(string target) : PhysicalFileSystem
    {
        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            destinationPath.Should().Be(target);
            overwrite.Should().BeFalse();
            File.WriteAllText(destinationPath, "user content won the create race");
            base.MoveFile(sourcePath, destinationPath, overwrite);
        }
    }
}
