// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetEffectBridgeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-dotnet-effects-").FullName;
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("CSharp", ".csproj", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "2.1.0", 2)]
    [InlineData("FSharp", ".fsproj", "Microsoft.Azure.Functions.Worker.Extensions.Http", "3.3.0", 3)]
    public async Task EvaluatedUpstreamActions_ExplicitCapability_PreviewThenCallsRealTypedHelperInOrder(
        string language, string extension, string packageId, string version, int effectCount)
    {
        NativeTemplateEffectPlan plan = await CreatePlanAsync(language, extension);
        DotNetEffectCapability capability = new(_runner, plan);
        NativeTemplateEffectWorkflow workflow = new();

        NativeTemplateEffectResult preview = await workflow.ExecuteAsync(plan, capability, dryRun: true, confirmed: false, CancellationToken.None);

        preview.Kind.Should().Be("previewed");
        _runner.ReceivedCalls().Should().BeEmpty();
        plan.Effects.Should().HaveCount(effectCount);
        plan.Effects[^1].Should().BeOfType<TemplateEditorNoOpEffect>();
        NativeTemplateEffectResult applied = await workflow.ExecuteAsync(plan, capability, dryRun: false, confirmed: true, CancellationToken.None);
        applied.Kind.Should().Be("applied");
        applied.Succeeded.Should().BeTrue();
        applied.CompletedEffects.Should().Equal(plan.Effects);
        string project = Path.Combine(_root, "project", "Selected" + extension);
        IReadOnlyList<string>[] commands = [.. _runner.ReceivedCalls().Select(call => (IReadOnlyList<string>)call.GetArguments()[0]!)];
        commands[0].Should().Equal("add", project, "package", packageId, "--version", version);
        if (language == "FSharp")
        {
            commands.Should().HaveCount(2);
            commands[1].Should().Equal("restore", project);
        }
        else
        {
            commands.Should().ContainSingle("the upstream C# HTTP template adds its package with implicit restore and no separate restore action");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluatedUpstreamActions_ChangedSourceOrTarget_RefusesBeforeAnyHelperCall(bool changeSource)
    {
        NativeTemplateEffectPlan plan = await CreatePlanAsync("FSharp", ".fsproj");
        DotNetEffectCapability capability = new(_runner, plan);
        string changed = changeSource ? plan.Effects[0].Source.Identity : plan.Effects[0].TargetPath;
        File.AppendAllText(changed, "changed");

        NativeTemplateEffectResult result = await new NativeTemplateEffectWorkflow().ExecuteAsync(
            plan, capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("preflightFailed");
        result.Succeeded.Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluatedFSharpActions_PackageFailure_RemainsPartialAndContinuesRestore()
    {
        NativeTemplateEffectPlan plan = await CreatePlanAsync("FSharp", ".fsproj");
        DotNetEffectCapability capability = new(_runner, plan);
        _runner.RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "add"),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DotnetCliException(1, "controlled restore failure", "", "add")));

        NativeTemplateEffectResult result = await new NativeTemplateEffectWorkflow().ExecuteAsync(
            plan, capability, dryRun: false, confirmed: true, CancellationToken.None);

        result.Kind.Should().Be("partialFailure");
        result.Succeeded.Should().BeFalse();
        result.FailedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplatePackageReferenceEffect>();
        result.CompletedEffects.Should().Contain(effect => effect is TemplateRestoreEffect);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("controlled restore failure", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("no rollback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluatedFSharpActions_CancellationInPackageAdd_DoesNotRunRestoreOrReportSuccess()
    {
        NativeTemplateEffectPlan plan = await CreatePlanAsync("FSharp", ".fsproj");
        DotNetEffectCapability capability = new(_runner, plan);
        using CancellationTokenSource cancellation = new();
        _runner.RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "add"), Arg.Any<string>(), cancellation.Token)
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            });
        Func<Task> action = () => new NativeTemplateEffectWorkflow().ExecuteAsync(
            plan, capability, dryRun: false, confirmed: true, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await _runner.DidNotReceive().RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "restore"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private async Task<NativeTemplateEffectPlan> CreatePlanAsync(string language, string extension)
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(archive, hive);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));
        Directory.CreateDirectory(output.Info.FullName);
        string project = Path.Combine(output.Info.FullName, "Selected" + extension);
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        using TemplateEngineSession session = new(new(output), hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Greeting", "Greeting", output.Info.FullName,
            new Dictionary<string, string?> { ["namespace"] = "Sample.Functions" }, dryRun: true, cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success);
        File.Exists(Path.Combine(output.Info.FullName, "Greeting" + (extension == ".fsproj" ? ".fs" : ".cs"))).Should().BeFalse();
        TemplateEffectSource source = new(template.Identity, archive, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
        NativeTemplateEffectPlanningResult planned = new NativeTemplateEffectPlanner()
            .PlanDependencies(output, source, preview.CreationEffects!.CreationResult, project);
        planned.Diagnostics.Should().BeEmpty();
        return planned.Plan!;
    }

    private sealed class DotNetEffectCapability(IDotnetCliRunner runner, NativeTemplateEffectPlan selectedPlan) : INativeTemplateEffectCapability
    {
        private readonly DotNetTemplateDependencyExecutor _executor = new(runner);
        private readonly IReadOnlyList<TemplateEffect> _selectedEffects = selectedPlan.Effects;
        private readonly TemplateEffectSource _source = selectedPlan.Effects[0].Source;
        private readonly string _project = selectedPlan.Effects[0].TargetPath;
        private string _targetDigest = Digest(selectedPlan.Effects[0].TargetPath);

        public Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> diagnostics = plan.Effects.SequenceEqual(_selectedEffects)
                && SourceMatches() && Digest(_project) == _targetDigest ? [] : ["Selected source or target snapshot changed."];
            return Task.FromResult(diagnostics);
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (effect.Source != _source || effect.TargetPath != _project || !SourceMatches() || Digest(_project) != _targetDigest)
            {
                return new(false, ["Selected source or target changed before dependency execution."]);
            }

            try
            {
                switch (effect)
                {
                    case TemplatePackageReferenceEffect package:
                        await _executor.AddPackageReferenceAsync(package.ProjectPath,
                            new(package.PackageId, package.Version, package.ProjectFileExtension), cancellationToken);
                        break;
                    case TemplateRestoreEffect restore:
                        await _executor.RestoreAsync(restore.ProjectPath, cancellationToken);
                        break;
                    default:
                        return new(false, ["The test capability does not execute this effect."]);
                }

                _targetDigest = Digest(_project);
                return new(true, []);
            }
            catch (DotnetCliException exception)
            {
                _targetDigest = Digest(_project);
                return new(false, [exception.Message], TargetMayHaveChanged: true);
            }
        }

        private bool SourceMatches() => Digest(_source.Identity) == _source.Sha256;

        private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
