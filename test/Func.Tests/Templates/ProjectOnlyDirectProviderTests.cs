// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.PowerShell;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(false, 0, "successful tidy stderr")]
    [InlineData(false, 17, "module resolution failed")]
    [InlineData(false, -1, "Go process unavailable")]
    [InlineData(true, 17, "must not execute")]
    public async Task DirectGo_ProductionProvider_CompletesAfterNativeConfigurationAndPreservesToolOutcome(
        bool skip, int exitCode, string stderr)
    {
        HostGoTool tool = new() { Outcome = (exitCode, stderr) };
        var preparation = new GoNativeProjectPreparation(_files, tool);
        INativeProjectWorkflowProvider provider = new GoProjectWorkflowProvider(preparation);
        using var journey = await OpenDirectProjectAsync("Go", provider, skip ? ["--skip-go-mod-tidy", "true"] : []);
        journey.Arguments.ValuesByCanonicalName["SkipGoModTidy"].Should().Be(skip ? "true" : "false");
        journey.Arguments.ExplicitParameterNames.Contains("SkipGoModTidy").Should().Be(skip);
        provider.Preparation.Should().BeSameAs(preparation);
        AssertDirectHandles(provider, journey.Selected);

        var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
        preview.Operations.Should().Equal(new NativeProjectPreparationStep(NativeProjectPreparationOperation.GoModTidy, skip));
        tool.Calls.Should().Be(0);
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        await journey.CreateAsync();
        AssertDirectConfig(journey, "go", "Go");
        File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "go.mod")).Should().Contain("module direct-app");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "local.settings.json")));
        settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString().Should().Be("native");
        var before = DirectOutputHashes(journey);
        var result = await preparation.CompleteAsync(journey.Context, preview, CancellationToken.None);
        result.Succeeded.Should().Be(skip || exitCode == 0);
        tool.Calls.Should().Be(skip ? 0 : 1);
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        if (skip) result.Diagnostics.Should().Equal("Skipped 'go mod tidy'.");
        else if (exitCode != 0)
            result.Diagnostics.Should().Equal(
                $"'go mod tidy' failed (exit {exitCode}). Generated project files and CLI configuration were retained.", stderr);
        else result.Diagnostics.Should().Equal(string.IsNullOrWhiteSpace(stderr) ? [] : [stderr]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectGo_CancellationAfterNativeCommit_ThrowsAndRetainsFiles(bool helperThrows)
    {
        using CancellationTokenSource cancellation = new();
        HostGoTool tool = new() { DuringExecution = cancellation.Cancel, ThrowCancellation = helperThrows };
        var preparation = new GoNativeProjectPreparation(_files, tool);
        using var journey = await OpenDirectProjectAsync("Go", new GoProjectWorkflowProvider(preparation), []);
        var preview = await preparation.PreviewAsync(journey.Context, cancellation.Token);
        await journey.CreateAsync();
        var before = DirectOutputHashes(journey);

        Func<Task> action = () => preparation.CompleteAsync(journey.Context, preview, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        tool.Calls.Should().Be(1);
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("target")]
    [InlineData("preview")]
    [InlineData("selected")]
    [InlineData("source")]
    [InlineData("manifest")]
    public async Task DirectGo_PostPreviewDrift_RefusesBeforeToolAndRetainsNativeCommit(string drift)
    {
        HostGoTool tool = new();
        var preparation = new GoNativeProjectPreparation(_files, tool);
        using var journey = await OpenDirectProjectAsync("Go", new GoProjectWorkflowProvider(preparation), []);
        var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
        preview.Succeeded.Should().BeTrue();
        await journey.CreateAsync();
        var context = journey.Context;
        if (drift == "parameters")
            context = context with { Parameters = new Dictionary<string, string?>(context.Parameters) { ["SkipGoModTidy"] = "true" } };
        else if (drift == "target")
            context = context with { TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "wrong-target")) };
        else if (drift == "preview") preview = preview with { };
        else if (drift == "selected")
            context = context with
            {
                SelectedTemplate = (await journey.WorkflowContext.Templater.ResolveGroupAsync(
                    "Azure.Functions.Go.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single(),
            };
        else if (drift == "source")
            await MutateDirectSelectedSourceAsync(journey.Selected, "content/project/main.go", text => text + "\n// changed");
        else
            await MutateDirectSelectedSourceAsync(journey.Selected, "provenance/go/project.json", text => text + "\n ");
        var before = DirectOutputHashes(journey);

        var result = await preparation.CompleteAsync(context, preview, CancellationToken.None);

        result.Succeeded.Should().BeFalse(drift);
        result.Diagnostics.Should().NotBeEmpty();
        tool.Calls.Should().Be(0);
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-bool")]
    public async Task DirectGo_MissingOrInvalidCanonicalSkip_RefusesWithoutNativeWrites(string? skip)
    {
        HostGoTool tool = new();
        var preparation = new GoNativeProjectPreparation(_files, tool);
        using var journey = await OpenDirectProjectAsync("Go", new GoProjectWorkflowProvider(preparation), []);
        var values = new Dictionary<string, string?>(journey.Context.Parameters);
        if (skip is null) values.Remove("SkipGoModTidy");
        else values["SkipGoModTidy"] = skip;

        var preview = await preparation.PreviewAsync(journey.Context with { Parameters = values }, CancellationToken.None);

        preview.Succeeded.Should().BeFalse();
        preview.Diagnostics.Should().Equal("Go preparation requires the declared canonical SkipGoModTidy value.");
        tool.Calls.Should().Be(0);
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go", "version")]
    [InlineData("Go", "source")]
    [InlineData("Go", "inventory")]
    [InlineData("Go", "configuration")]
    [InlineData("PowerShell", "version")]
    [InlineData("PowerShell", "source")]
    [InlineData("PowerShell", "inventory")]
    [InlineData("PowerShell", "configuration")]
    [InlineData("Java", "version")]
    [InlineData("Java", "source")]
    [InlineData("Java", "inventory")]
    [InlineData("Java", "configuration")]
    public async Task DirectProviders_SelectedSourceAndManifestDrift_RefuseBeforeOwnedEffects(string stack, string mutation)
    {
        HostGoTool go = new();
        HostAzLookup az = new("12");
        INativeProjectWorkflowProvider provider = DirectProvider(stack, _files, go, az);
        using var journey = await OpenDirectProjectAsync(stack, provider, []);
        AssertDirectHandles(provider, journey.Selected);
        string sourcePath = stack switch
        {
            "Go" => "content/project/main.go",
            "Java" => "content/project/pom.xml",
            _ => "content/project/profile.ps1",
        };
        string path = mutation == "source" ? sourcePath : mutation == "configuration"
            ? "content/project/.template.config/template.json" : $"provenance/{stack.ToLowerInvariant()}/project.json";
        await MutateDirectSelectedSourceAsync(journey.Selected, path, text =>
        {
            if (mutation is "source" or "configuration") return text + "\n ";
            var manifest = JsonNode.Parse(text)!;
            if (mutation == "version") manifest["packageVersion"] = "999.0.0";
            else manifest["sources"]!.AsArray().RemoveAt(0);
            return manifest.ToJsonString();
        });

        if (provider.Preparation is { } preparation)
        {
            var refused = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
            refused.Succeeded.Should().BeFalse(mutation);
            refused.Diagnostics.Should().NotBeEmpty();
        }
        else
        {
            var refused = await provider.PrepareAsync(journey.WorkflowContext, CancellationToken.None);
            refused.Request.Should().BeNull(mutation);
            refused.Diagnostics.Should().ContainSingle().Which.Should().Contain("Cannot qualify");
        }
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        go.Calls.Should().Be(0);
        az.Lookups.Should().Be(0);
        az.SourceReads.Should().Be(0);
    }

    [Theory]
    [InlineData("Go", "files")]
    [InlineData("Go", "configuration")]
    [InlineData("Go", "worker")]
    [InlineData("PowerShell", "files")]
    [InlineData("PowerShell", "configuration")]
    [InlineData("PowerShell", "worker")]
    public async Task DirectPreparations_IncompleteOrMismatchedNativeCommit_RefusesWithoutHelperOrRollback(string stack, string mutation)
    {
        HostGoTool go = new();
        HostAzLookup az = new("12");
        var provider = DirectProvider(stack, _files, go, az);
        using var journey = await OpenDirectProjectAsync(stack, provider, []);
        var preparation = provider.Preparation!;
        var preview = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
        preview.Succeeded.Should().BeTrue();
        await journey.CreateAsync();
        if (mutation == "files")
            File.Delete(Path.Combine(journey.Output.Info.FullName, stack == "Go" ? "main.go" : "profile.ps1"));
        else if (mutation == "configuration")
            File.WriteAllText(Path.Combine(journey.Output.Info.FullName, ".func", "config.json"),
                """{"stack":{"runtime":"java","language":"Java"}}""");
        else
        {
            var path = Path.Combine(journey.Output.Info.FullName, "local.settings.json");
            var settings = JsonNode.Parse(File.ReadAllText(path))!;
            settings["Values"]!["FUNCTIONS_WORKER_RUNTIME"] = "wrong";
            File.WriteAllText(path, settings.ToJsonString());
        }
        var before = DirectOutputHashes(journey);

        var result = await preparation.CompleteAsync(journey.Context, preview, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
        DirectOutputHashes(journey).Should().BeEquivalentTo(before);
        go.Calls.Should().Be(0);
        az.Lookups.Should().Be(0);
        az.SourceReads.Should().Be(0);
    }

    [Fact]
    public async Task DirectJava_SourceQualifiedReady_PreservesInvocationWithoutPreparationOrSecondInitializer()
    {
        Action nullDependency = () => new JavaProjectWorkflowProvider(null!);
        nullDependency.Should().Throw<ArgumentNullException>();
        INativeProjectWorkflowProvider provider = new JavaProjectWorkflowProvider(_files);
        using var journey = await OpenDirectProjectAsync("Java", provider, []);
        AssertDirectHandles(provider, journey.Selected);

        var ready = await provider.PrepareAsync(journey.WorkflowContext, CancellationToken.None);

        ready.Request.Should().BeSameAs(journey.Request);
        ready.Diagnostics.Should().BeEmpty();
        provider.Preparation.Should().BeNull("Java init has no Maven or second initializer preparation");
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        await journey.CreateAsync();
        AssertDirectConfig(journey, "java", "Java");
        File.ReadAllText(Path.Combine(journey.Output.Info.FullName, "pom.xml"))
            .Should().Contain("<artifactId>direct-app</artifactId>").And.Contain("<functionAppName>direct-app</functionAppName>");
        File.Exists(Path.Combine(journey.Output.Info.FullName, "src", "main", "java", "com", "function", "Function.java"))
            .Should().BeTrue();
        Directory.Exists(Path.Combine(journey.Output.Info.FullName, "target")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("PowerShell")]
    [InlineData("Java")]
    public async Task DirectProviders_NullAndCancellation_AreNotSuccessShapedResults(string stack)
    {
        HostGoTool go = new();
        HostAzLookup az = new("12");
        var provider = DirectProvider(stack, _files, go, az);
        using var journey = await OpenDirectProjectAsync(stack, provider, []);
        Func<Task> nullContext = () => provider.PrepareAsync(null!, CancellationToken.None);
        await nullContext.Should().ThrowAsync<ArgumentNullException>();
        Action nullEntry = () => provider.Handles(null!);
        nullEntry.Should().Throw<ArgumentNullException>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> canceled = () => provider.PrepareAsync(journey.WorkflowContext, cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        if (provider.Preparation is { } preparation)
        {
            Func<Task> preview = () => preparation.PreviewAsync(journey.Context, cancellation.Token);
            await preview.Should().ThrowAsync<OperationCanceledException>();
            Func<Task> nullPreview = () => preparation.PreviewAsync(null!, CancellationToken.None);
            await nullPreview.Should().ThrowAsync<ArgumentNullException>();
            var valid = await preparation.PreviewAsync(journey.Context, CancellationToken.None);
            Func<Task> nullCompletion = () => preparation.CompleteAsync(null!, valid, CancellationToken.None);
            await nullCompletion.Should().ThrowAsync<ArgumentNullException>();
            Func<Task> missingPreview = () => preparation.CompleteAsync(journey.Context, null!, CancellationToken.None);
            await missingPreview.Should().ThrowAsync<ArgumentNullException>();
            Func<Task> completion = () => preparation.CompleteAsync(journey.Context, valid, cancellation.Token);
            await completion.Should().ThrowAsync<OperationCanceledException>();
        }
        Directory.Exists(journey.Output.Info.FullName).Should().BeFalse();
        go.Calls.Should().Be(0);
        az.Lookups.Should().Be(0);
    }

    private async Task<DirectProjectJourney> OpenDirectProjectAsync(string stack, INativeProjectWorkflowProvider provider,
        IReadOnlyList<string> tokens, string suffix = "direct", string? outputPath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var services = await InstallAsync(stack, cancellationToken);
        var output = WorkingDirectory.FromExplicit(outputPath ?? Path.Combine(_root, suffix));
        var templater = ProjectTemplater(services, output, stack.ToLowerInvariant());
        var resolution = await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, cancellationToken);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success, Diagnostics(resolution));
        var selected = resolution.Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), tokens), cancellationToken);
        parsed.IsComplete.Should().BeTrue();
        var invocation = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Direct App!");
        var command = new InitExecutionRequest(output, selected.Entry.Identity, invocation.Name, provider.Stack, stack,
            false, true, false, false, tokens, new HashSet<string>());
        var workflow = new NativeProjectWorkflowContext(templater, selected, parsed, command, invocation);
        var ready = await provider.PrepareAsync(workflow, cancellationToken);
        ready.Diagnostics.Should().BeEmpty();
        ready.Request.Should().BeSameAs(invocation);
        var preview = await selected.InvokeAsync(invocation with { DryRun = true }, cancellationToken);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.ProjectConfigurations.Should().ContainSingle();
        return new(services, templater, selected, parsed, invocation, workflow,
            new(selected, output, parsed.ValuesByCanonicalName, preview));
    }

    private static INativeProjectWorkflowProvider DirectProvider(string stack, IFileSystem files, HostGoTool go, HostAzLookup az)
        => stack switch
        {
            "Go" => new GoProjectWorkflowProvider(new(files, go)),
            "PowerShell" => new PowerShellProjectWorkflowProvider(new(files, az)),
            "Java" => new JavaProjectWorkflowProvider(files),
            _ => throw new ArgumentOutOfRangeException(nameof(stack)),
        };

    private static void AssertDirectHandles(INativeProjectWorkflowProvider provider, ResolvedTemplate selected)
    {
        provider.Handles(selected.Entry).Should().BeTrue();
        foreach (var entry in new[]
        {
            selected.Entry with { Identity = "another-project" },
            selected.Entry with { Language = "another-language" },
            selected.Entry with { Type = TemplateType.Item },
            selected.Entry with { Package = null },
            selected.Entry with { Package = new("Other.Package", selected.Entry.Package!.Version) },
            selected.Entry with { Package = selected.Entry.Package! with { Version = null } },
        })
            provider.Handles(entry).Should().BeFalse();
    }

    private static void AssertDirectConfig(DirectProjectJourney journey, string runtime, string language)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(journey.Output.Info.FullName, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be(runtime);
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
    }

    private static Dictionary<string, string> DirectOutputHashes(DirectProjectJourney journey)
        => Directory.GetFiles(journey.Output.Info.FullName, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(journey.Output.Info.FullName, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private static async Task MutateDirectSelectedSourceAsync(ResolvedTemplate selected, string path, Func<string, string> mutate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archivePath = selected.ConfigurationSourceIdentity!.Split("::", StringSplitOptions.None)[0];
        File.Exists(archivePath).Should().BeTrue("the mutation must target the selected installed archive, not its input copy");
        var before = await selected.ReadPackageFileAsync(path, cancellationToken);
        using var originalStream = new MemoryStream(before.ToArray(), writable: false);
        using var originalReader = new StreamReader(originalStream);
        var changed = mutate(await originalReader.ReadToEndAsync(cancellationToken));
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(path)!;
            entry.Should().NotBeNull();
            entry.Delete();
            using StreamWriter writer = new(archive.CreateEntry(path).Open(), new UTF8Encoding(before.Span.StartsWith(Encoding.UTF8.Preamble)));
            writer.Write(changed);
        }
        var after = await selected.ReadPackageFileAsync(path, cancellationToken);
        using var changedStream = new MemoryStream(after.ToArray(), writable: false);
        using var changedReader = new StreamReader(changedStream);
        (await changedReader.ReadToEndAsync(cancellationToken)).Should().Be(changed);
        after.ToArray().Should().NotEqual(before.ToArray());
    }

    private sealed class DirectProjectJourney(ServiceProvider services, Templater templater, ResolvedTemplate selected,
        TemplateCandidateParseResult arguments, TemplateInvocationRequest request, NativeProjectWorkflowContext workflowContext,
        NativeProjectPreparationContext context) : IDisposable
    {
        public ResolvedTemplate Selected { get; } = selected;
        public TemplateCandidateParseResult Arguments { get; } = arguments;
        public TemplateInvocationRequest Request { get; } = request;
        public NativeProjectWorkflowContext WorkflowContext { get; } = workflowContext;
        public NativeProjectPreparationContext Context { get; } = context;
        public WorkingDirectory Output => Request.OutputDirectory;

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            var result = await Selected.InvokeAsync(Request, cancellationToken);
            result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
            result.ConfiguredProjects.Should().Equal(Output.Info.FullName);
            result.Effects.Should().BeNull();
        }

        public void Dispose()
        {
            templater.Dispose();
            services.Dispose();
        }
    }
}
