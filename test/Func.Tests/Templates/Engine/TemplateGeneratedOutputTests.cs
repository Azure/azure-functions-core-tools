// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateGeneratedOutputTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-generated-output-").FullName;

    private string Hive => Path.Combine(_root, "hive");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvokeAsync_GeneratedPath_AdmitsOnlyActualCapturedOutputsAndExplicitRealForce(bool occupied, bool force)
    {
        var package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        Directory.CreateDirectory(Path.Combine(package, "TOKEN"));
        File.WriteAllText(Path.Combine(package, "TOKEN", "Created.txt"), "CONTENT");
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), """
            {"identity":"Func.Tests.ImageSequence","name":"Image sequence","shortName":"image-sequence","tags":{"type":"item"},
             "symbols":{"Path":{"type":"generated","generator":"image-sequence","fileRename":"TOKEN"}},
             "primaryOutputs":[{"path":"TOKEN/Created.txt"}]}
            """);
        await TemplateFixtures.InstallAsync(package, Hive);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "LogicalApp"));
        string target = Path.Combine(output.Info.FullName, "1", "Created.txt");
        if (occupied)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "original");
        }
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = new SequenceMacro();
        session.Settings.Components.AddComponent(typeof(IMacro), sequence);
        session.Settings.Components.AddComponent(typeof(IGeneratedSymbolMacro), sequence);
        using Templater templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.ImageSequence", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: force), CancellationToken.None);

        result.Status.Should().Be(occupied && !force ? TemplateInvocationStatus.FileConflict : TemplateInvocationStatus.Success, result.Diagnostic);
        var change = result.Changes.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)).Should().Be(target);
        change.Kind.Should().Be(occupied ? TemplateFileChangeKind.Overwrite : TemplateFileChangeKind.Create);
        result.HasCommittedOutput.Should().Be(!occupied || force);
        sequence.Evaluations.Should().Be(2, "native effects and creation evaluate once, with no native render during installation");
        var native = session.NativeResult!;
        Path.GetFullPath(Path.Combine(output.Info.FullName, native.CreationEffects!.FileChanges.Single().TargetRelativePath))
            .Should().Be(Path.Combine(output.Info.FullName, "0", "Created.txt"));
        Path.GetFullPath(Path.Combine(output.Info.FullName, native.CreationResult!.PrimaryOutputs.Single().Path)).Should().Be(target);
        File.Exists(Path.Combine(output.Info.FullName, "0", "Created.txt")).Should().BeFalse();
        File.ReadAllText(target).Should().Be(occupied && !force ? "original" : "CONTENT");
    }

    [Fact]
    public async Task NativeHost_DestructiveChangeWithoutForce_DoesNotReplaceCliConflictAdmission()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), Hive);
        var output = Directory.CreateDirectory(Path.Combine(_root, "LogicalApp")).FullName;
        var target = Path.Combine(output, "LogicalApp.txt");
        File.WriteAllText(target, "original");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);

        var native = await new TemplateCreator(session.Settings).InstantiateAsync(template, null, "LogicalApp", output,
            new Dictionary<string, string?>(), forceCreation: false, cancellationToken: CancellationToken.None);

        native.Status.Should().Be(CreationResultStatus.Success);
        native.CreationEffects!.FileChanges.Should().Contain(change => change.ChangeKind != ChangeKind.Create);
        File.ReadAllText(target).Should().Be("LogicalApp content");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class RecordingSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        public ITemplateCreationResult? NativeResult { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(
            ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            NativeResult = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            return NativeResult;
        }
    }

    private sealed class SequenceMacro : IGeneratedSymbolMacro
    {
        public Guid Id { get; } = new("6c1fd328-eaab-45ac-8d02-81470778ac7d");
        public string Type => "image-sequence";
        public int Evaluations { get; private set; }
        public IMacroConfig CreateConfig(IEngineEnvironmentSettings environmentSettings, IGeneratedSymbolConfig config) => new SequenceConfig(config.VariableName);
        public void EvaluateConfig(IEngineEnvironmentSettings environmentSettings, IVariableCollection variables, IMacroConfig config)
            => variables[config.VariableName] = Evaluations++ == 0 ? "0" : "1";
    }

    private sealed record SequenceConfig(string VariableName) : IMacroConfig
    {
        public string Type => "image-sequence";
    }
}
