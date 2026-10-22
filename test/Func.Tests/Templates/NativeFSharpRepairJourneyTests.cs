// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeFSharpRepairJourneyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-fsharp-repair-journey-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ResolvedPinnedItem_ExplicitRepair_PreviewsWithoutMutationThenCompilesBeforeSelectedProjectRestore()
    {
        using Journey journey = await CreateAsync();
        byte[] original = File.ReadAllBytes(journey.Project);

        TemplateInvocationResult preview = await journey.Selected.InvokeAsync(journey.Request with { DryRun = true }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Plan.Effects[0].Should().BeOfType<TemplateFSharpCompileEffect>();
        preview.Effects.Plan.Effects[2].Should().BeOfType<TemplateRestoreEffect>().Which.ProjectPath.Should().Be(journey.Project);
        File.Exists(journey.Generated).Should().BeFalse();
        File.ReadAllBytes(journey.Project).Should().Equal(original);
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
        TemplateInvocationResult created = await journey.Selected.InvokeAsync(journey.Request, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Effects!.CompletedEffects.Should().HaveCount(4);
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("RepairedHttp.fs", "Program.fs");
        IReadOnlyList<string>[] commands = [.. journey.DependencyRunner.ReceivedCalls().Select(call => (IReadOnlyList<string>)call.GetArguments()[0]!)];
        commands.Should().HaveCount(2);
        commands[0].Should().Equal("add", journey.Project, "package", "Microsoft.Azure.Functions.Worker.Extensions.Http", "--version", "3.3.0");
        commands[1].Should().Equal("restore", journey.Project);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolvedPinnedItem_ActualRepairAndDependencies_BuildMetadataContainsHttpFunction()
    {
        using Journey journey = await CreateAsync(realDependencies: true);
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
        }

        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(Path.GetDirectoryName(journey.Project)!, "NuGet.Config"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        TemplateInvocationResult created = await journey.Selected.InvokeAsync(journey.Request, timeout.Token);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("RepairedHttp.fs", "Program.fs");
        await journey.Runner.RunAsync(["build", journey.Project, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"],
            Path.GetDirectoryName(journey.Project), timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(journey.Project)!,
            "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString()).Should().Contain("RepairedHttp");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("projectDigest")]
    [InlineData("generatedHash")]
    [InlineData("generatedPath")]
    [InlineData("framework")]
    [InlineData("duplicate")]
    public async Task QualifiedRepair_ChangedOrUnqualifiedContract_RefusesBeforeNativeCreation(string invalid)
    {
        using Journey journey = await CreateAsync();
        TemplateFSharpCompileEffect original = journey.Request.EffectSupport!.FSharpRepair!;
        TemplateFSharpCompileEffect repair = new(invalid == "source" ? original.Source with { Sha256 = new string('0', 64) } : original.Source,
            original.ProjectPath, invalid == "projectDigest" ? new string('0', 64) : original.ExpectedProjectSha256,
            original.EvaluatedCompileOrder, invalid == "duplicate" ? [.. original.GeneratedFiles, .. original.GeneratedFiles]
                : [new(invalid == "generatedPath" ? Path.Combine(_root, "Other.fs") : journey.Generated,
                    invalid == "generatedHash" ? new string('0', 64) : original.GeneratedFiles[0].Sha256)]);
        journey.Capability.ExpectedRepair = original;
        TemplateInvocationRequest request = journey.Request with
        {
            EffectSupport = journey.Request.EffectSupport with { FSharpRepair = repair },
        };
        if (invalid == "framework")
        {
            NativeMsBuildBindingSnapshot binding = journey.Request.MsBuildBindings!;
            request = request with { MsBuildBindings = new(binding.ProjectFilePath, binding.TemplateIdentity, binding.SourceIdentity,
                binding.SourceFingerprint, "net8.0", binding.RootNamespace) };
        }

        byte[] before = File.ReadAllBytes(journey.Project);
        TemplateInvocationResult result = await journey.Selected.InvokeAsync(request, CancellationToken.None);

        result.Status.Should().NotBe(TemplateInvocationStatus.Success);
        File.Exists(journey.Generated).Should().BeFalse();
        File.ReadAllBytes(journey.Project).Should().Equal(before);
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("project")]
    [InlineData("source")]
    [InlineData("updaterFailure")]
    public async Task CompileEffectFailure_AfterNativeGeneration_RetainsSourceAndStopsDependencies(string failure)
    {
        FailingFiles files = new() { FailCommit = failure == "updaterFailure" };
        using Journey journey = await CreateAsync(files: files);
        journey.Capability.BeforeCompile = () =>
        {
            if (failure == "project")
            {
                File.AppendAllText(journey.Project, "\n<!-- concurrent project edit -->");
            }
            else if (failure == "source")
            {
                File.AppendAllText(journey.Generated, "\n// changed source");
            }
        };

        TemplateInvocationResult result = await journey.Selected.InvokeAsync(journey.Request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Effects!.Kind.Should().Be("partialFailure");
        result.Effects.FailedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateFSharpCompileEffect>();
        File.Exists(journey.Generated).Should().BeTrue();
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("Program.fs");
    }

    [Fact]
    public async Task DependencyFailure_AfterCompileRepair_PreservesRepairedOrderAndReportsPartialOutcome()
    {
        using Journey journey = await CreateAsync();
        journey.DependencyRunner.RunAsync(Arg.Is<IReadOnlyList<string>>(args => args[0] == "add"),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new DotnetCliException(1, "package failed", "", "add")));

        TemplateInvocationResult result = await journey.Selected.InvokeAsync(journey.Request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Effects!.CompletedEffects.Should().Contain(effect => effect is TemplateFSharpCompileEffect);
        result.Effects.FailedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplatePackageReferenceEffect>();
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("RepairedHttp.fs", "Program.fs");
    }

    [Fact]
    public async Task MissingOptIn_PreservesOriginalCompileOmissionRatherThanSilentlyRepairing()
    {
        using Journey journey = await CreateAsync();
        TemplateInvocationRequest request = journey.Request with
        {
            EffectSupport = journey.Request.EffectSupport! with { FSharpRepair = null },
        };
        INativeTemplateEffectCapability onlyDependencies = Substitute.For<INativeTemplateEffectCapability>();
        onlyDependencies.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        onlyDependencies.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns(new TemplateEffectExecutionResult(true, []));
        request = request with { EffectSupport = request.EffectSupport! with { Capability = onlyDependencies } };

        TemplateInvocationResult result = await journey.Selected.InvokeAsync(request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        result.Effects!.Plan.Effects.Should().NotContain(effect => effect is TemplateFSharpCompileEffect);
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("Program.fs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OccupiedOrPreviouslyGeneratedTarget_RefusesBeforeRepairOrDependencies(bool alreadyApplied)
    {
        using Journey journey = await CreateAsync();
        if (alreadyApplied)
        {
            (await journey.Selected.InvokeAsync(journey.Request, CancellationToken.None)).Status.Should().Be(TemplateInvocationStatus.Success);
            journey.DependencyRunner.ClearReceivedCalls();
        }
        else
        {
            File.WriteAllText(journey.Generated, "user-owned source");
        }
        byte[] project = File.ReadAllBytes(journey.Project);
        byte[] generated = File.ReadAllBytes(journey.Generated);

        TemplateInvocationResult refused = await journey.Selected.InvokeAsync(journey.Request with { Force = true }, CancellationToken.None);

        refused.Status.Should().NotBe(TemplateInvocationStatus.Success);
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
        File.ReadAllBytes(journey.Project).Should().Equal(project);
        File.ReadAllBytes(journey.Generated).Should().Equal(generated);
    }

    [Fact]
    public async Task Cancellation_AfterNativeGeneration_PreservesSourceAndDoesNotRunDependencies()
    {
        using Journey journey = await CreateAsync();
        using CancellationTokenSource cancellation = new();
        journey.Capability.BeforeCompile = cancellation.Cancel;
        Func<Task> action = () => journey.Selected.InvokeAsync(journey.Request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(journey.Generated).Should().BeTrue();
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
        (await CompileAsync(journey.Runner, journey.Project)).Should().Equal("Program.fs");
    }

    [SymbolicLinkFact]
    public async Task SourceBecomesLink_AfterNativeGeneration_RefusesRepairAndPreservesExternalBytes()
    {
        using Journey journey = await CreateAsync();
        string outside = Path.Combine(_root, "outside.fs");
        File.WriteAllText(outside, "external sentinel");
        journey.Capability.BeforeCompile = () =>
        {
            File.Delete(journey.Generated);
            File.CreateSymbolicLink(journey.Generated, outside);
        };

        TemplateInvocationResult result = await journey.Selected.InvokeAsync(journey.Request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllText(outside).Should().Be("external sentinel");
        journey.DependencyRunner.ReceivedCalls().Should().BeEmpty();
    }

    private async Task<Journey> CreateAsync(bool realDependencies = false, FSharpProjectFiles? files = null)
    {
        string outer = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "native.nupkg");
        using (ZipArchive package = ZipFile.OpenRead(outer))
        {
            package.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(archive, hive);
        string projectRoot = Path.Combine(_root, "project");
        var directory = WorkingDirectory.FromExplicit(projectRoot);
        TemplateEngineSession session = new(new(directory, new(directory, "dotnet", "F#")), hive);
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo projectTemplate = templates.Single(template => template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.FSharp.Isolated.3.x");
        ITemplateInfo http = templates.Single(template => template.Identity == "Azure.Function.FSharp.Isolated.HttpTrigger.3.x");
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult created = await creator.InstantiateAsync(projectTemplate, "App", "App", projectRoot,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success);
        string project = Path.GetFullPath(Path.Combine(projectRoot, created.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        IDotnetPathResolver executable = Substitute.For<IDotnetPathResolver>();
        executable.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(executable);
        DotNetTemplateProjectContext evaluated = await new DotNetTemplateProjectPreparation(runner).ReadAsync(project, CancellationToken.None);
        string expansion = Path.Combine(_root, "native-expansion");
        Dictionary<string, string?> arguments = new() { ["namespace"] = evaluated.RootNamespace, ["AccessRights"] = "Anonymous" };
        ITemplateCreationResult expanded = await creator.InstantiateAsync(http, "RepairedHttp", "RepairedHttp", expansion,
            arguments, cancellationToken: CancellationToken.None);
        expanded.Status.Should().Be(CreationResultStatus.Success);
        Templater resolver = TemplateFixtures.CreateTemplater(session);
        ResolvedTemplate selected = (await resolver.ResolveGroupAsync(http.Identity, TemplateType.Item, CancellationToken.None)).Group![0];
        string generated = Path.Combine(projectRoot, "RepairedHttp.fs");
        TemplateEffectSource source = new(selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!);
        TemplateFSharpCompileEffect repair = new(source, project, Hash(project), [Path.Combine(projectRoot, "Program.fs")],
            [new(generated, Hash(Path.Combine(expansion, "RepairedHttp.fs")))]);
        IDotnetCliRunner dependencyRunner = Substitute.For<IDotnetCliRunner>();
        RepairCapability capability = new(new(runner, files ?? new()), new(realDependencies ? runner : dependencyRunner), selected, repair);
        TemplateInvocationRequest request = new(directory, arguments, "RepairedHttp")
        {
            MsBuildBindings = new(project, source.TemplateIdentity, source.Identity, source.Sha256, evaluated.TargetFramework, evaluated.RootNamespace),
            EffectSupport = new(directory, project, capability, Confirmed: true) { FSharpRepair = repair },
        };
        return new(resolver, selected, request, capability, runner, dependencyRunner, project, generated);
    }

    private static async Task<string?[]> CompileAsync(IDotnetCliRunner runner, string project)
    {
        StringBuilder output = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], Path.GetDirectoryName(project),
            line => output.AppendLine(line), null, CancellationToken.None);
        using var items = JsonDocument.Parse(output.ToString());
        return [.. items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray().Select(item => item.GetProperty("Identity").GetString())];
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Journey(Templater Resolver, ResolvedTemplate Selected, TemplateInvocationRequest Request,
        RepairCapability Capability, DotnetCliRunner Runner, IDotnetCliRunner DependencyRunner, string Project, string Generated) : IDisposable
    {
        public void Dispose() => Resolver.Dispose();
    }

    private sealed class FailingFiles : FSharpProjectFiles
    {
        public bool FailCommit { get; init; }

        public override Task ReplaceAsync(string projectPath, string expectedSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
            => FailCommit ? Task.FromException(new IOException("controlled atomic updater failure"))
                : base.ReplaceAsync(projectPath, expectedSha256, content, cancellationToken);
    }

    private sealed class RepairCapability(FSharpCompileUpdater updater, DotNetTemplateDependencyExecutor dependencies,
        ResolvedTemplate selected, TemplateFSharpCompileEffect expected) : INativeTemplateEffectCapability
    {
        private FSharpCompileUpdatePlan? _plan;

        public TemplateFSharpCompileEffect ExpectedRepair { get; set; } = expected;
        public Action? BeforeCompile { get; set; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            if (await selected.ValidateConfigurationAsync(cancellationToken) is not null
                || plan.Effects[0] is not TemplateFSharpCompileEffect repair || repair != ExpectedRepair)
            {
                return ["The selected repair source or expected repair contract changed."];
            }

            try
            {
                _plan = await updater.PlanAsync(repair.ProjectPath, repair.ExpectedProjectSha256, repair.EvaluatedCompileOrder,
                    [.. repair.GeneratedFiles.Select(file => new FSharpGeneratedFile(file.Path, file.Sha256))], cancellationToken);
                return [];
            }
            catch (InvalidOperationException exception)
            {
                return [exception.Message];
            }
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            if (await selected.ValidateConfigurationAsync(cancellationToken) is not null)
            {
                return new(false, ["The selected native source changed."]);
            }

            try
            {
                switch (effect)
                {
                    case TemplateFSharpCompileEffect:
                        BeforeCompile?.Invoke();
                        await updater.ApplyAsync(_plan ?? throw new InvalidOperationException("Repair was not preflighted."), cancellationToken);
                        break;
                    case TemplatePackageReferenceEffect package:
                        await dependencies.AddPackageReferenceAsync(package.ProjectPath,
                            new(package.PackageId, package.Version, package.ProjectFileExtension), cancellationToken);
                        break;
                    case TemplateRestoreEffect restore:
                        await dependencies.RestoreAsync(restore.ProjectPath, cancellationToken);
                        break;
                    default:
                        return new(false, ["Unsupported effect in the repair test capability."]);
                }

                return new(true, []);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or DotnetCliException)
            {
                return new(false, [exception.Message], TargetMayHaveChanged: true);
            }
        }
    }
}
