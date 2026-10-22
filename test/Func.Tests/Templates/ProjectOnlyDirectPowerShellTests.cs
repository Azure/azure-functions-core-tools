// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PowerShell;
using HostPowerShellPreparation = Azure.Functions.Cli.Templates.PowerShell.Shared.PowerShellProjectPreparation;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(false, null, null)]
    [InlineData(false, null, "7.6")]
    [InlineData(true, "12", "7.4")]
    [InlineData(true, "14", "7.6")]
    [InlineData(true, null, "7.4")]
    [InlineData(true, null, "7.6")]
    public async Task DirectPowerShell_ProductionPreparation_FreezesOneLookupAndCreateOnlyContent(
        bool managed, string? major, string? runtime)
    {
        HostAzLookup lookup = new(major);
        var preparation = new PowerShellNativeProjectPreparation(_files, lookup);
        INativeProjectWorkflowProvider provider = new PowerShellProjectWorkflowProvider(preparation);
        List<string> tokens = [];
        if (managed) tokens.AddRange(["--managed-dependencies", "true"]);
        if (runtime is not null) tokens.AddRange(["--runtime-version", runtime]);
        using var journey = await OpenDirectProjectAsync("PowerShell", provider, tokens);
        AssertDirectHandles(provider, journey.Selected);
        provider.Preparation.Should().BeSameAs(preparation);
        journey.Arguments.ExplicitParameterNames.Contains("ManagedDependencies").Should().Be(managed);
        journey.Arguments.ExplicitParameterNames.Contains("RuntimeVersion").Should().Be(runtime is not null);
        journey.Context.Parameters["ManagedDependencies"].Should().Be(managed ? "true" : "false");
        journey.Context.Parameters["RuntimeVersion"].Should().Be(runtime ?? "7.4");
        var original = await journey.Selected.ReadPackageFileAsync(
            "content/project/.template.config/requirements.psd1", CancellationToken.None);
        var source = Encoding.UTF8.GetString(original.Span);
        source.Should().Contain("# 'Az' = 'MAJOR_VERSION.*'");
        var expected = major is null ? source : source.Replace("MAJOR_VERSION", major, StringComparison.Ordinal);

        var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
        preview.Operations.Should().Equal(new NativeProjectPreparationStep(
            NativeProjectPreparationOperation.PowerShellManagedDependencies, !managed));
        lookup.Lookups.Should().Be(managed ? 1 : 0);
        lookup.SourceReads.Should().Be(managed ? 1 : 0);
        preview.FileEffects!.Effects.Should().HaveCount(managed ? 1 : 0);
        if (managed)
        {
            var effect = preview.FileEffects.Effects.Single().Should().BeOfType<TemplateCreateFileEffect>().Subject;
            effect.Content.Should().Be(expected);
            effect.Source.Identity.Should().Be("Azure.Functions.Cli.Workloads.PowerShell.Templates.requirements.psd1");
            effect.Source.TemplateIdentity.Should().Be("Azure.Functions.PowerShell.Basic");
            effect.Source.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(original.Span)));
            effect.TargetPath.Should().Be(Path.Combine(journey.Output.Info.FullName, "requirements.psd1"));
        }
        if (managed && major is null)
            preview.Diagnostics.Should().Equal(
                "Could not resolve the latest stable Az module major version. The requirements.psd1 Az entry remains commented with MAJOR_VERSION; set a supported version before enabling it.");
        else preview.Diagnostics.Should().BeEmpty();
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        await journey.CreateAsync();
        AssertDirectConfig(journey, "powershell", "PowerShell");
        var target = Path.Combine(journey.Output.Info.FullName, "requirements.psd1");
        File.Exists(target).Should().BeFalse("native creation reserves rather than emits requirements");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "local.settings.json")));
        settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME_VERSION").GetString().Should().Be(runtime ?? "7.4");
        var before = DirectOutputHashes(journey);
        var result = await preparation.CompleteAsync(journey.Context, preview, CancellationToken.None);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Diagnostics));
        lookup.Lookups.Should().Be(managed ? 1 : 0);
        lookup.SourceReads.Should().Be(managed ? 2 : 0, "completion revalidates source but never repeats lookup");
        if (managed)
        {
            File.ReadAllText(target).Should().Be(expected);
            result.Effects!.Plan.Should().BeEquivalentTo(preview.FileEffects);
            result.Effects.CompletedEffects.Should().ContainSingle();
            result.Effects.FailedEffects.Should().BeEmpty();
            result.Effects.Kind.Should().Be("applied");
            result.Diagnostics.Should().Equal(preview.Diagnostics);
            var repeated = await preparation.CompleteAsync(journey.Context, preview, CancellationToken.None);
            repeated.Succeeded.Should().BeFalse("requirements is create-only, including repeated completion");
            File.ReadAllText(target).Should().Be(expected);
        }
        else
        {
            File.Exists(target).Should().BeFalse();
            result.Effects.Should().BeNull();
            DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        }
        foreach (var pair in before)
            DirectOutputHashes(journey)[pair.Key].Should().Be(pair.Value);
    }

    [Theory]
    [InlineData("6.0")]
    [InlineData("7.5")]
    public async Task DirectPowerShell_UnsupportedRuntime_RefusesBeforeLookupOrWrites(string runtime)
    {
        HostAzLookup lookup = new("12");
        var preparation = new PowerShellNativeProjectPreparation(_files, lookup);
        using var journey = await OpenDirectProjectAsync("PowerShell", new PowerShellProjectWorkflowProvider(preparation), []);
        var parameters = new Dictionary<string, string?>(journey.Context.Parameters)
        {
            ["ManagedDependencies"] = "true",
            ["RuntimeVersion"] = runtime,
        };

        var preview = await preparation.PreviewAsync(journey.Context with { Parameters = parameters }, CancellationToken.None);

        preview.Succeeded.Should().BeFalse();
        preview.Diagnostics.Should().ContainSingle().Which.Should().Contain("7.4 or 7.6");
        lookup.Lookups.Should().Be(0);
        lookup.SourceReads.Should().Be(0);
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        var native = await journey.Selected.InvokeAsync(journey.Request with { Parameters = parameters }, CancellationToken.None);
        native.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("target")]
    [InlineData("preview")]
    [InlineData("selected")]
    [InlineData("source")]
    [InlineData("manifest")]
    [InlineData("helperHash")]
    public async Task DirectPowerShell_PostPreviewDrift_RefusesAndRetainsCommittedNativeFiles(string drift)
    {
        DirectRequirementsLookup lookup = new();
        var preparation = new PowerShellNativeProjectPreparation(_files, lookup);
        using var journey = await OpenDirectProjectAsync("PowerShell", new PowerShellProjectWorkflowProvider(preparation),
            ["--managed-dependencies", "true"]);
        var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
        preview.Succeeded.Should().BeTrue();
        await journey.CreateAsync();
        var context = journey.Context;
        if (drift == "parameters")
            context = context with { Parameters = new Dictionary<string, string?>(context.Parameters) { ["NoBundles"] = "true" } };
        else if (drift == "target")
            context = context with { TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "wrong-target")) };
        else if (drift == "preview") preview = preview with { };
        else if (drift == "selected")
            context = context with
            {
                SelectedTemplate = (await journey.WorkflowContext.Templater.ResolveGroupAsync(
                    "Azure.Functions.PowerShell.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single(),
            };
        else if (drift == "source")
            await MutateDirectSelectedSourceAsync(journey.Selected, "content/project/.template.config/requirements.psd1", text => text + "\n# changed");
        else if (drift == "manifest")
            await MutateDirectSelectedSourceAsync(journey.Selected, "provenance/powershell/project.json", text => text + "\n ");
        else lookup.ChangeSource = true;
        var before = DirectOutputHashes(journey);

        var result = await preparation.CompleteAsync(context, preview, CancellationToken.None);

        result.Succeeded.Should().BeFalse(drift);
        result.Diagnostics.Should().NotBeEmpty();
        lookup.Lookups.Should().Be(1);
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        File.Exists(Path.Combine(journey.Output.Info.FullName, "requirements.psd1")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DirectPowerShell_OccupiedDestinationBeforePreviewOrCompletion_PreservesUserData(bool afterPreview, bool directory)
    {
        HostAzLookup lookup = new("12");
        var preparation = new PowerShellNativeProjectPreparation(_files, lookup);
        using var journey = await OpenDirectProjectAsync("PowerShell", new PowerShellProjectWorkflowProvider(preparation),
            ["--managed-dependencies", "true"]);
        var target = Path.Combine(journey.Output.Info.FullName, "requirements.psd1");
        NativeProjectPreparationPreview? preview = afterPreview
            ? await preparation.PreviewAsync(journey.Context, CancellationToken.None) : null;
        if (afterPreview) await journey.CreateAsync();
        Directory.CreateDirectory(journey.Output.Info.FullName);
        if (directory)
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "user.txt"), "occupied directory");
        }
        else File.WriteAllText(target, "occupied file");
        var before = DirectOutputHashes(journey);

        if (afterPreview)
        {
            var result = await preparation.CompleteAsync(journey.Context, preview!, CancellationToken.None);
            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().Contain(message => message.Contains("occupied", StringComparison.Ordinal));
        }
        else
        {
            var result = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().Contain(message => message.Contains("occupied", StringComparison.Ordinal));
        }
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        lookup.Lookups.Should().Be(1);
    }

    [Theory]
    [InlineData("winner")]
    [InlineData("ioFailure")]
    [InlineData("cancel")]
    public async Task DirectPowerShell_ProductionAtomicMaterializer_ReportsFailureOrCancellationWithoutRollback(string outcome)
    {
        using CancellationTokenSource cancellation = new();
        DirectRequirementsFileSystem files = new(outcome, cancellation);
        HostAzLookup lookup = new("12");
        var preparation = new PowerShellNativeProjectPreparation(files, lookup);
        using var journey = await OpenDirectProjectAsync("PowerShell", new PowerShellProjectWorkflowProvider(preparation),
            ["--managed-dependencies", "true"]);
        var preview = await preparation.PreviewAsync(journey.Context, cancellation.Token);
        preview.Succeeded.Should().BeTrue();
        await journey.CreateAsync();
        var before = DirectOutputHashes(journey);

        if (outcome == "cancel")
        {
            Func<Task> action = () => preparation.CompleteAsync(journey.Context, preview, cancellation.Token);
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await preparation.CompleteAsync(journey.Context, preview, cancellation.Token);
            result.Succeeded.Should().BeFalse();
            result.Effects!.Kind.Should().Be("partialFailure");
            result.Effects.CompletedEffects.Should().BeEmpty();
            result.Effects.FailedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateCreateFileEffect>();
            result.Diagnostics.Should().Contain(message => message.Contains("create-only write failed", StringComparison.Ordinal));
        }
        files.Moves.Should().Be(1, "the real physical materializer must reach its atomic commit");
        var target = Path.Combine(journey.Output.Info.FullName, "requirements.psd1");
        if (outcome == "winner") File.ReadAllText(target).Should().Be("user won the atomic race");
        else File.Exists(target).Should().BeFalse();
        Directory.GetFiles(journey.Output.Info.FullName, "requirements.psd1.tmp.*").Should().BeEmpty();
        foreach (var pair in before)
            DirectOutputHashes(journey)[pair.Key].Should().Be(pair.Value);
        lookup.Lookups.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectPowerShell_CancellationDuringLookupOrAfterNativeCommit_ThrowsWithHonestFileState(bool afterCommit)
    {
        using CancellationTokenSource cancellation = new();
        HostAzLookup lookup = new("12") { DuringLookup = afterCommit ? null : cancellation.Cancel };
        var preparation = new PowerShellNativeProjectPreparation(_files, lookup);
        using var journey = await OpenDirectProjectAsync("PowerShell", new PowerShellProjectWorkflowProvider(preparation),
            ["--managed-dependencies", "true"]);
        if (afterCommit)
        {
            var preview = await preparation.PreviewAsync(journey.Context, cancellation.Token);
            await journey.CreateAsync();
            var before = DirectOutputHashes(journey);
            cancellation.Cancel();
            Func<Task> action = () => preparation.CompleteAsync(journey.Context, preview, cancellation.Token);
            await action.Should().ThrowAsync<OperationCanceledException>();
            DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        }
        else
        {
            Func<Task> action = () => preparation.PreviewAsync(journey.Context, cancellation.Token);
            await action.Should().ThrowAsync<OperationCanceledException>();
            Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        }
        lookup.Lookups.Should().Be(1);
        File.Exists(Path.Combine(journey.Output.Info.FullName, "requirements.psd1")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("PowerShell")]
    public async Task DirectPreparations_UnexpectedHelperFault_IsNotConvertedToUserFailure(string stack)
    {
        InvalidOperationException fault = new("unexpected direct helper fault");
        HostGoTool go = new() { Failure = fault };
        HostAzLookup az = new("12") { Failure = fault };
        var provider = DirectProvider(stack, _files, go, az);
        using var journey = await OpenDirectProjectAsync(stack, provider,
            stack == "PowerShell" ? ["--managed-dependencies", "true"] : []);
        var preparation = provider.Preparation!;
        if (stack == "Go")
        {
            var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
            await journey.CreateAsync();
            var before = DirectOutputHashes(journey);
            Func<Task> action = () => preparation.CompleteAsync(journey.Context, preview, CancellationToken.None);
            (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(fault);
            DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        }
        else
        {
            Func<Task> action = () => preparation.PreviewAsync(journey.Context, CancellationToken.None);
            (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(fault);
            Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        }
    }

    private sealed class DirectRequirementsLookup : HostPowerShellPreparation
    {
        public int Lookups { get; private set; }
        public bool ChangeSource { get; set; }

        public override Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            return Task.FromResult<string?>("12");
        }

        public override string ReadRequirementsTemplate()
            => base.ReadRequirementsTemplate() + (ChangeSource ? "\n# linked source changed" : string.Empty);
    }

    private sealed class DirectRequirementsFileSystem(string outcome, CancellationTokenSource cancellation) : PhysicalFileSystem
    {
        public int Moves { get; private set; }

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            Path.GetFileName(destinationPath).Should().Be("requirements.psd1");
            File.Exists(sourcePath).Should().BeTrue();
            overwrite.Should().BeFalse();
            Moves++;
            if (outcome == "cancel")
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            if (outcome == "ioFailure") throw new IOException("deterministic atomic commit failure");
            File.WriteAllText(destinationPath, "user won the atomic race");
            base.MoveFile(sourcePath, destinationPath, overwrite);
        }
    }
}
