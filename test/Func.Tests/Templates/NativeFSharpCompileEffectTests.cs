// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeFSharpCompileEffectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-fsharp-effect-" + Guid.NewGuid().ToString("N"));
    private readonly NativeTemplateEffectPlanner _planner = new();

    private WorkingDirectory ProjectDirectory => WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));
    private string ProjectPath => Path.Combine(ProjectDirectory.Info.FullName, "Selected.fsproj");
    private string ProgramPath => Path.Combine(ProjectDirectory.Info.FullName, "Program.fs");
    private string GeneratedPath => Path.Combine(ProjectDirectory.Info.FullName, "Named.fs");

    [Fact]
    public async Task Plan_ActualNativeCreationEffects_OrdersRequiredCompileBeforePackageAndSelectedProjectRestore()
    {
        var native = await PreviewAsync();
        var repair = Repair(native.Source);

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, repair);

        result.Diagnostics.Should().BeEmpty();
        result.Plan!.Effects.Should().HaveCount(4);
        result.Plan.Effects[0].Should().BeSameAs(repair);
        result.Plan.Effects[0].ContinueOnError.Should().BeFalse();
        result.Plan.Effects[1].Should().BeOfType<TemplatePackageReferenceEffect>().Which.ProjectPath.Should().Be(ProjectPath);
        result.Plan.Effects[2].Should().Be(new TemplateRestoreEffect(native.Source, ProjectPath, true));
        result.Plan.Effects[3].Should().BeOfType<TemplateEditorNoOpEffect>();
        native.Effects.CreationResult.PrimaryOutputs.Should().ContainSingle().Which.Path.Should().EndWith("Named.fs");
        File.Exists(GeneratedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Plan_DefaultDependencyPath_DoesNotInventCompileRepair()
    {
        var native = await PreviewAsync();

        var result = _planner.PlanDependencies(ProjectDirectory, native.Source, native.Effects.CreationResult, ProjectPath);

        result.Plan!.Effects.Should().HaveCount(3).And.NotContain(effect => effect is TemplateFSharpCompileEffect);
        result.Plan.Effects.OfType<TemplateRestoreEffect>().Should().ContainSingle().Which.ProjectPath.Should().Be(ProjectPath);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("source")]
    [InlineData("fingerprint")]
    [InlineData("project")]
    public async Task Plan_MismatchedSelection_RejectsBeforeAnyCapabilityDispatch(string mismatch)
    {
        var native = await PreviewAsync();
        TemplateEffectSource supplied = native.Source with
        {
            TemplateIdentity = mismatch == "identity" ? "Other.Template" : native.Source.TemplateIdentity,
            Identity = mismatch == "source" ? "other-mounted-source" : native.Source.Identity,
            Sha256 = mismatch == "fingerprint" ? new string('a', 64) : native.Source.Sha256,
        };
        string project = mismatch == "project" ? Path.Combine(ProjectDirectory.Info.FullName, "Other.fsproj") : ProjectPath;

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, project, Repair(supplied));

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("current selected source", StringComparison.Ordinal));
        File.Exists(GeneratedPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("generated")]
    [InlineData("digest")]
    [InlineData("project-digest")]
    [InlineData("non-fsharp-project")]
    [InlineData("compile-order")]
    [InlineData("duplicate-order")]
    [InlineData("equivalent-order")]
    [InlineData("program")]
    [InlineData("nested")]
    [InlineData("outside")]
    public async Task Plan_UnqualifiedProjectSequenceOrGeneratedFiles_Rejects(string invalid)
    {
        var native = await PreviewAsync();
        string generated = invalid switch
        {
            "generated" => Path.Combine(ProjectDirectory.Info.FullName, "NotGenerated.fs"),
            "program" => ProgramPath,
            "nested" => Path.Combine(ProjectDirectory.Info.FullName, "nested", "Named.fs"),
            "outside" => Path.Combine(_root, "Outside.fs"),
            _ => GeneratedPath,
        };
        var repair = new TemplateFSharpCompileEffect(native.Source,
            invalid == "non-fsharp-project" ? Path.ChangeExtension(ProjectPath, ".csproj") : ProjectPath,
            invalid == "project-digest" ? "missing-digest" : Digest("original project"),
            invalid switch
            {
                "compile-order" => [Path.Combine(ProjectDirectory.Info.FullName, "NotProgram.fs")],
                "duplicate-order" => [ProgramPath, ProgramPath],
                "equivalent-order" => [Path.Combine(ProjectDirectory.Info.FullName, ".", "Program.fs"), ProgramPath],
                _ => [ProgramPath],
            },
            [new(generated, invalid == "digest" ? "missing-digest" : Digest("native generated bytes"))]);

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, repair.ProjectPath, repair);

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Plan_ExistingNativeFSharpOutput_RejectsOverwriteEvenWithControlledCapability()
    {
        var native = await PreviewAsync(existingOutput: true);
        string original = File.ReadAllText(GeneratedPath);

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, Repair(native.Source));

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("overwrites");
        File.ReadAllText(GeneratedPath).Should().Be(original);
    }

    [Theory]
    [InlineData("""{"files":"0"}""")]
    [InlineData("""{"targetFiles":["Named.fs"]}""")]
    [InlineData("""{"projectFile":"Selected.fsproj"}""")]
    public async Task Plan_RestoreWithArguments_RemainsUnhandledRatherThanBlanketRetargeting(string restoreArgs)
    {
        var native = await PreviewAsync(restoreArgs: restoreArgs);

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, Repair(native.Source));

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("not handled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_UnknownAction_RejectsEntireRepairBeforeProjectMutation()
    {
        var native = await PreviewAsync(unknownAction: true);

        var result = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, Repair(native.Source));

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("not handled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Workflow_CompileFailure_StopsBeforePackageOrRestoreAndPreservesFailureShape()
    {
        var native = await PreviewAsync();
        var repair = Repair(native.Source);
        var plan = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, repair).Plan!;
        var capability = Capability();
        capability.ExecuteAsync(repair, Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(false, ["Project digest changed."], TargetMayHaveChanged: false));

        var result = await new NativeTemplateEffectWorkflow().ExecuteAsync(plan, capability,
            dryRun: false, confirmed: true, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("partialFailure");
        result.CompletedEffects.Should().BeEmpty();
        result.FailedEffects.Should().Equal(repair);
        result.Diagnostics.Should().Contain("Project digest changed.");
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplatePackageReferenceEffect>(), Arg.Any<CancellationToken>());
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateRestoreEffect>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workflow_PreviewOrRefusal_NeverDispatchesCompileMutation(bool dryRun)
    {
        var native = await PreviewAsync();
        var plan = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, Repair(native.Source)).Plan!;
        var capability = Capability();

        var result = await new NativeTemplateEffectWorkflow().ExecuteAsync(plan, capability,
            dryRun, confirmed: false, CancellationToken.None);

        result.Kind.Should().Be(dryRun ? "previewed" : "declined");
        result.CompletedEffects.Should().BeEmpty();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_ExplicitRepairExecutesBeforeDependencyActions()
    {
        var native = await PreviewAsync();
        var plan = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, Repair(native.Source)).Plan!;
        var capability = Capability();
        List<TemplateEffect> dispatched = [];
        capability.ExecuteAsync(Arg.Do<TemplateEffect>(dispatched.Add), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(true, []));

        var result = await new NativeTemplateEffectWorkflow().ExecuteAsync(plan, capability,
            dryRun: false, confirmed: true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        dispatched.Should().HaveCount(3);
        dispatched[0].Should().BeOfType<TemplateFSharpCompileEffect>();
        dispatched[1].Should().BeOfType<TemplatePackageReferenceEffect>();
        dispatched[2].Should().BeOfType<TemplateRestoreEffect>().Which.ProjectPath.Should().Be(ProjectPath);
    }

    [Fact]
    public async Task Renderer_ExplicitCompilePreview_ReportsExactProjectSequenceAndHashedGeneratedFiles()
    {
        var native = await PreviewAsync();
        var repair = Repair(native.Source);
        var plan = _planner.PlanFSharpItemDependencies(ProjectDirectory, native.Source, native.Effects,
            ProjectDirectory, ProjectPath, repair).Plan!;
        var effects = await new NativeTemplateEffectWorkflow().ExecuteAsync(plan, Capability(),
            dryRun: true, confirmed: false, CancellationToken.None);
        TestInteractionService interaction = new();
        TemplateCatalogEntry selected = new(native.Source.TemplateIdentity, "Selected F# item", null, TemplateType.Item,
            [], null, "F#", 0, null, false, [], null);
        NewExecutionResult result = new("previewed", true)
        {
            SelectedTemplate = selected,
            Invocation = new(TemplateInvocationStatus.Success, []) { Effects = effects },
        };

        int exit = new NewExecutionRenderer(interaction).Render(result, jsonOutput: true);

        exit.Should().Be(0);
        using var json = JsonDocument.Parse(interaction.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        var effect = json.RootElement.GetProperty("invocation").GetProperty("effects").GetProperty("planned")[0];
        effect.GetProperty("targetPath").GetString().Should().Be(ProjectPath);
        effect.GetProperty("expectedProjectSha256").GetString().Should().Be(repair.ExpectedProjectSha256);
        effect.GetProperty("evaluatedCompileOrder")[0].GetString().Should().Be(ProgramPath);
        effect.GetProperty("generatedFiles")[0].GetProperty("path").GetString().Should().Be(GeneratedPath);
        effect.GetProperty("generatedFiles")[0].GetProperty("sha256").GetString().Should().Be(repair.GeneratedFiles[0].Sha256);
        json.RootElement.GetProperty("invocation").GetProperty("effects").GetProperty("completed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Effect_CopiesInputCollectionsAndRejectsNullLists()
    {
        string[] order = [ProgramPath];
        TemplateGeneratedFSharpFile[] files = [new(GeneratedPath, Digest("generated"))];
        var effect = new TemplateFSharpCompileEffect(new("Selected", "source", Digest("source")),
            ProjectPath, Digest("project"), order, files);
        order[0] = "caller mutation";
        files[0] = new("caller mutation", "invalid");

        effect.EvaluatedCompileOrder.Should().Equal(ProgramPath);
        effect.GeneratedFiles.Should().Equal(new TemplateGeneratedFSharpFile(GeneratedPath, Digest("generated")));
        Action noOrder = () => new TemplateFSharpCompileEffect(effect.Source, ProjectPath, Digest("project"), null!, []);
        Action noFiles = () => new TemplateFSharpCompileEffect(effect.Source, ProjectPath, Digest("project"), [], null!);
        noOrder.Should().Throw<ArgumentNullException>();
        noFiles.Should().Throw<ArgumentNullException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TemplateFSharpCompileEffect Repair(TemplateEffectSource source) =>
        new(source, ProjectPath, Digest("original project"), [ProgramPath], [new(GeneratedPath, Digest("native generated bytes"))]);

    private async Task<(TemplateEffectSource Source, ICreationEffects Effects)> PreviewAsync(
        bool existingOutput = false, string restoreArgs = "{}", bool unknownAction = false)
    {
        string package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        File.WriteAllText(Path.Combine(package, "ItemFunction.fs"), "type ItemFunction = class end");
        string unknown = unknownAction ? """,{"actionId":"f3ba64f5-68a9-4ac4-9cfe-4519e4940a77","continueOnError":true}""" : string.Empty;
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), $$$$"""
            {"identity":"Controlled.FSharp.Http","name":"Controlled F# HTTP shape","shortName":"controlled-fsharp-http",
             "tags":{"type":"item","language":"F#"},"sourceName":"ItemFunction","primaryOutputs":[{"path":"ItemFunction.fs"}],
             "postActions":[
               {"actionId":"{{{{NativeTemplateEffectPlanner.PackageReferenceActionId}}}}","continueOnError":true,
                "args":{"referenceType":"package","reference":"Microsoft.Azure.Functions.Worker.Extensions.Http","version":"3.3.0",
                  "projectFileExtensions":".fsproj"}},
               {"actionId":"{{{{NativeTemplateEffectPlanner.RestoreActionId}}}}","continueOnError":true,"args":{{{{restoreArgs}}}}},
               {"actionId":"{{{{NativeTemplateEffectPlanner.EditorActionId}}}}","continueOnError":true,"args":{"files":"0"}}
               {{{{unknown}}}}]}
            """);
        if (existingOutput)
        {
            Directory.CreateDirectory(ProjectDirectory.Info.FullName);
            File.WriteAllText(GeneratedPath, "user-owned file");
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive, CancellationToken.None);
        using TemplateEngineSession session = new(new(ProjectDirectory, new(ProjectDirectory, "dotnet", "F#")), hive);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        var configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        var native = await new TemplateCreator(session.Settings).InstantiateAsync(template, "Named", null, ProjectDirectory.Info.FullName,
            new Dictionary<string, string?>(), forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);
        native.Status.Should().Be(CreationResultStatus.Success);
        return (new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint), native.CreationEffects!);
    }

    private static INativeTemplateEffectCapability Capability()
    {
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(true, []));
        return capability;
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
