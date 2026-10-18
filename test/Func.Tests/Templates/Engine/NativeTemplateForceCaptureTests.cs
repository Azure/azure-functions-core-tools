// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeTemplateForceCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-force-capture-").FullName;

    [Theory]
    [InlineData("CSharp", "DurableFunctionsEntityClass.1.x")]
    [InlineData("CSharp", "DurableFunctionsEntityFunction.1.x")]
    [InlineData("CSharp", "DurableFunctionsOrchestration.1.x")]
    [InlineData("CSharp", "SignalR.3.x")]
    [InlineData("CSharp", "DaprPublishOutputBinding")]
    [InlineData("CSharp", "DaprServiceInvocationTrigger")]
    [InlineData("CSharp", "HttpTrigger.3.x")]
    [InlineData("FSharp", "HttpTrigger.3.x")]
    public async Task Capture_ActualItemForceOverwrite_ExpandsPrivatelyAndRetainsRealTargetEffectsWithoutWrites(string language, string item)
    {
        using TemplateEngineSession session = await OpenAsync(language);
        ITemplateInfo template = await SelectAsync(session, language, item);
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        string extension = language == "FSharp" ? ".fs" : ".cs";
        string project = Path.Combine(target, language == "FSharp" ? "Sample.fsproj" : "Sample.csproj");
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns(new TemplateEffectExecutionResult(true, []));
        TemplateInvocationRequest request = new(WorkingDirectory.FromExplicit(target),
            new Dictionary<string, string?> { ["namespace"] = "Sample.Functions" }, "Greeting", DryRun: true, Force: true)
        {
            EffectSupport = new(WorkingDirectory.FromExplicit(target), project, capability, Confirmed: true),
            MsBuildBindings = new(project, template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}",
                configuration.Fingerprint, "net10.0", "Sample.Functions"),
        };
        NativeTemplatePreparationResult plan = await session.CaptureNativePreparationAsync(
            template, configuration, request, new PhysicalFileSystem(), expandFiles: false, CancellationToken.None);
        plan.Failure.Should().BeNull(plan.Failure?.Diagnostic);
        string primary = plan.Snapshot!.Effects.CreationResult.PrimaryOutputs.Single().Path;
        Directory.CreateDirectory(Path.Combine(target, ".git"));
        File.WriteAllText(Path.Combine(target, ".git", "HEAD"), "preserve repository");
        File.WriteAllText(Path.Combine(target, primary), "preserve until explicit creation");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(Path.Combine(target, "unrelated.txt"), "not a root cleanup");
        var before = Snapshot(target);
        string[] directories = Directory.GetDirectories(target, "*", SearchOption.AllDirectories);

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(
            template, configuration, request, new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Failure.Should().BeNull(captured.Failure?.Diagnostic);
        captured.Snapshot!.Effects.FileChanges.Should().ContainSingle().Which.ChangeKind.Should().BeOneOf(ChangeKind.Change, ChangeKind.Overwrite);
        var expanded = captured.Snapshot.ExpandedFiles.Should().ContainSingle().Subject;
        Path.GetFileName(expanded.Key).Should().Be(Path.GetFileName(primary));
        Path.GetExtension(primary).Should().Be(extension);
        Encoding.UTF8.GetString(expanded.Value.Span).Should().Contain("Sample.Functions").And.NotContain("preserve until explicit creation");
        Snapshot(target).Should().Equal(before);
        Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Should().Equal(directories);

        NativeTemplatePreparationResult empty = await session.CaptureNativePreparationAsync(
            template, configuration, request with { OutputDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "empty")) },
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);
        empty.Failure.Should().BeNull(empty.Failure?.Diagnostic);
        empty.Snapshot!.Effects.FileChanges.Should().ContainSingle().Which.ChangeKind.Should().Be(ChangeKind.Create);
        empty.Snapshot.ExpandedFiles.Single().Value.ToArray().Should().Equal(expanded.Value.ToArray());
        Directory.Exists(Path.Combine(_root, "empty")).Should().BeFalse();
        Snapshot(target).Should().Equal(before);
        Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Should().Equal(directories);
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());

        TemplateInvocationResult preview = await session.InvokeAsync(template, configuration, request, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        Snapshot(target).Should().Equal(before);
        Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Should().Equal(directories);
        TemplateInvocationResult created = await session.InvokeAsync(template, configuration, request with { DryRun = false }, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllBytes(Path.Combine(target, primary)).Should().Equal(expanded.Value.ToArray());
        File.ReadAllText(project).Should().Be("<Project />");
        File.ReadAllText(Path.Combine(target, "unrelated.txt")).Should().Be("not a root cleanup");
        File.ReadAllText(Path.Combine(target, ".git", "HEAD")).Should().Be("preserve repository");
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("FSharp")]
    public async Task Capture_ActualItemOccupiedWithoutForce_RemainsRefusedWithoutWrites(string language)
    {
        using TemplateEngineSession session = await OpenAsync(language);
        ITemplateInfo template = await SelectAsync(session, language, "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        string file = Path.Combine(target, language == "FSharp" ? "Greeting.fs" : "Greeting.cs");
        File.WriteAllText(file, "unforced user content");

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Greeting", DryRun: true),
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Snapshot.Should().BeNull();
        captured.Failure.Should().NotBeNull();
        File.ReadAllText(file).Should().Be("unforced user content");
    }

    [Theory]
    [InlineData("CSharp", ".csproj", ".cs")]
    [InlineData("FSharp", ".fsproj", ".fs")]
    public async Task Capture_ActualHttpForce_PreviewAndInvokeUseSameRenderedBytesAndControlledEffects(
        string language, string projectExtension, string sourceExtension)
    {
        using TemplateEngineSession session = await OpenAsync(language);
        ITemplateInfo template = await SelectAsync(session, language, "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        string output = Path.Combine(target, "Greeting" + sourceExtension);
        File.WriteAllText(output, "original user source");
        string project = Path.Combine(target, "Sample" + projectExtension);
        File.WriteAllText(project, "<Project />");
        var before = Snapshot(target);
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns(new TemplateEffectExecutionResult(true, []));
        TemplateInvocationRequest request = new(WorkingDirectory.FromExplicit(target),
            new Dictionary<string, string?> { ["namespace"] = "Sample.Functions" }, "Greeting", DryRun: true, Force: true)
        {
            EffectSupport = new(WorkingDirectory.FromExplicit(target), project, capability, Confirmed: true),
            MsBuildBindings = new(project, template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}",
                configuration.Fingerprint, "net10.0", "Sample.Functions"),
        };

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(
            template, configuration, request, new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);
        captured.Failure.Should().BeNull(captured.Failure?.Diagnostic);
        TemplateInvocationResult preview = await session.InvokeAsync(template, configuration, request, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Changes.Should().ContainSingle().Which.Kind.Should().BeOneOf("Change", "Overwrite");
        Snapshot(target).Should().Equal(before);
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());

        TemplateInvocationResult created = await session.InvokeAsync(template, configuration, request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllBytes(output).Should().Equal(captured.Snapshot!.ExpandedFiles.Single().Value.ToArray());
        File.ReadAllText(project).Should().Be("<Project />");
        capability.ReceivedCalls().Should().Contain(call => call.GetMethodInfo().Name == nameof(INativeTemplateEffectCapability.ExecuteAsync));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("missing-file")]
    [InlineData("directory")]
    [InlineData("file-ancestor")]
    [InlineData("escape")]
    [InlineData("placeholder")]
    public async Task Capture_ForcedNonRegularOrUncontainedEffects_RemainRefusedBeforePrivateCreation(string invalid)
    {
        string relative = invalid switch
        {
            "file-ancestor" => "blocked/Greeting.cs",
            "escape" => "../outside.cs",
            _ => "Greeting.cs",
        };
        using CaptureSession session = await OpenAsync("CSharp", preview => ReplaceChange(preview,
            invalid == "delete" ? ChangeKind.Delete : ChangeKind.Overwrite, relative, invalid == "placeholder" ? "-.-" : null));
        ITemplateInfo template = await SelectAsync(session, "CSharp", "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "user.txt"), "preserve unrelated");
        if (invalid == "directory") Directory.CreateDirectory(Path.Combine(target, "Greeting.cs"));
        else if (invalid == "file-ancestor") File.WriteAllText(Path.Combine(target, "blocked"), "not a directory");
        else if (invalid == "escape") File.WriteAllText(Path.Combine(_root, "outside.cs"), "outside boundary");
        else if (invalid != "missing-file") File.WriteAllText(Path.Combine(target, "Greeting.cs"), "preserve source");
        var before = Snapshot(target);
        string[] directories = Directory.GetDirectories(target, "*", SearchOption.AllDirectories);

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Greeting", DryRun: true, Force: true),
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Snapshot.Should().BeNull();
        captured.Failure.Should().NotBeNull();
        session.CreationCalls.Should().Be(0);
        Snapshot(target).Should().Equal(before);
        Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Should().Equal(directories);
        if (invalid == "escape") File.ReadAllText(Path.Combine(_root, "outside.cs")).Should().Be("outside boundary");
    }

    [Theory]
    [InlineData(ChangeKind.Change)]
    [InlineData(ChangeKind.Overwrite)]
    public async Task Capture_ForcedRegularFileChangeKinds_RetainsRealKindAndCreatesOnlyPrivateFiles(ChangeKind kind)
    {
        using CaptureSession session = await OpenAsync("CSharp", preview => ReplaceChange(preview, kind,
            preview.CreationEffects!.FileChanges.Single().TargetRelativePath));
        ITemplateInfo template = await SelectAsync(session, "CSharp", "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Greeting.cs"), "retain until actual creation");
        var before = Snapshot(target);

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Greeting", DryRun: true, Force: true),
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Failure.Should().BeNull(captured.Failure?.Diagnostic);
        captured.Snapshot!.Effects.FileChanges.Should().ContainSingle().Which.ChangeKind.Should().Be(kind);
        captured.Snapshot.ExpandedFiles.Should().ContainSingle();
        session.CreationCalls.Should().Be(1);
        Snapshot(target).Should().Equal(before);
    }

    [SymbolicLinkFact]
    public async Task Capture_ForcedLinkedOutputAncestor_RefusesWithoutFollowingLinkOrExpanding()
    {
        using CaptureSession session = await OpenAsync("CSharp", preview => ReplaceChange(preview, ChangeKind.Overwrite, "linked/Greeting.cs"));
        ITemplateInfo template = await SelectAsync(session, "CSharp", "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "Greeting.cs"), "preserve linked content");
        Directory.CreateSymbolicLink(Path.Combine(target, "linked"), outside);

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Greeting", DryRun: true, Force: true),
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Snapshot.Should().BeNull();
        captured.Failure!.Diagnostic.Should().Contain("symbolic link or reparse point");
        session.CreationCalls.Should().Be(0);
        File.ReadAllText(Path.Combine(outside, "Greeting.cs")).Should().Be("preserve linked content");
    }

    [SymbolicLinkFact]
    public async Task Capture_ActualForcedHttpThenOutputBecomesLink_InvokeRefusesBeforeOverwritingOrExecutingEffects()
    {
        using TemplateEngineSession session = await OpenAsync("CSharp");
        ITemplateInfo template = await SelectAsync(session, "CSharp", "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        string output = Path.Combine(target, "Greeting.cs");
        string outside = Path.Combine(_root, "outside.cs");
        string project = Path.Combine(target, "Sample.csproj");
        File.WriteAllText(output, "original source");
        File.WriteAllText(outside, "preserve external source");
        File.WriteAllText(project, "<Project />");
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));
        TemplateInvocationRequest request = new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(),
            "Greeting", DryRun: true, Force: true)
        {
            EffectSupport = new(WorkingDirectory.FromExplicit(target), project, capability, Confirmed: true),
            MsBuildBindings = new(project, template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}",
                configuration.Fingerprint, "net10.0", "Sample.Functions"),
        };
        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(
            template, configuration, request, new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);
        captured.Failure.Should().BeNull(captured.Failure?.Diagnostic);
        File.Delete(output);
        File.CreateSymbolicLink(output, outside);

        TemplateInvocationResult invoked = await session.InvokeAsync(template, configuration, request with { DryRun = false }, CancellationToken.None);

        invoked.Status.Should().Be(TemplateInvocationStatus.Failed);
        invoked.Diagnostic.Should().Contain("symbolic link or reparse point");
        File.ReadAllText(outside).Should().Be("preserve external source");
        File.ReadAllText(project).Should().Be("<Project />");
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_ForcedPrivateOutputHasUnexpectedFileOrEffect_RefusesDetachedSnapshot(bool effectDrift)
    {
        using CaptureSession session = await OpenAsync("CSharp", afterCreation: (directory, native) =>
        {
            if (effectDrift) return ReplaceChange(native, ChangeKind.Create, "unexpected.cs");
            File.WriteAllText(Path.Combine(directory, "unexpected.cs"), "not an authorized output");
            return native;
        });
        ITemplateInfo template = await SelectAsync(session, "CSharp", "HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string target = Path.Combine(_root, "project");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Greeting.cs"), "preserve real source");
        var before = Snapshot(target);

        NativeTemplatePreparationResult captured = await session.CaptureNativePreparationAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Greeting", DryRun: true, Force: true),
            new PhysicalFileSystem(), expandFiles: true, CancellationToken.None);

        captured.Snapshot.Should().BeNull();
        captured.Failure.Should().NotBeNull();
        Snapshot(target).Should().Equal(before);
        session.CreationCalls.Should().Be(1);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<CaptureSession> OpenAsync(string language, Func<ITemplateCreationResult, ITemplateCreationResult>? transformPreview = null,
        Func<string, ITemplateCreationResult, ITemplateCreationResult>? afterCreation = null)
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "dotnet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(archive, hive, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));
        return new(new(output, new(output, "dotnet", language == "FSharp" ? "F#" : "C#")), hive, transformPreview, afterCreation);
    }

    private static async Task<ITemplateInfo> SelectAsync(TemplateEngineSession session, string language, string item) =>
        (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.{item}");

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private static ITemplateCreationResult ReplaceChange(ITemplateCreationResult native, ChangeKind kind, string path, string? sourcePath = null)
    {
        var change = Substitute.For<IFileChange2>();
        change.ChangeKind.Returns(kind);
        change.TargetRelativePath.Returns(path);
        change.SourceRelativePath.Returns(sourcePath ?? native.CreationEffects!.FileChanges.OfType<IFileChange2>().Single().SourceRelativePath);
        var effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(native.CreationEffects!.CreationResult);
        effects.FileChanges.Returns(new IFileChange[] { change });
        var result = Substitute.For<ITemplateCreationResult>();
        result.Status.Returns(native.Status);
        result.CreationEffects.Returns(effects);
        return result;
    }

    private sealed class CaptureSession(TemplateEngineContext context, string hive,
        Func<ITemplateCreationResult, ITemplateCreationResult>? transformPreview,
        Func<string, ITemplateCreationResult, ITemplateCreationResult>? afterCreation) : TemplateEngineSession(context, hive)
    {
        public int CreationCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            ITemplateCreationResult native = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (dryRun) return transformPreview?.Invoke(native) ?? native;
            CreationCalls++;
            return afterCreation?.Invoke(request.OutputDirectory.Info.FullName, native) ?? native;
        }
    }
}
