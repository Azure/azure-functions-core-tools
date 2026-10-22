// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetProjectBindingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-bind-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeHttp_ActualImportedProjectProperties_BindsEvaluatedNet10BeforeNativeActionConditions()
    {
        DotNetTemplateProjectContext evaluated = await ReadProjectAsync();
        string archive = ExtractArchive();
        NativeMsBuildBindingSnapshot snapshot = new(evaluated.ProjectFilePath, "Azure.Function.CSharp.Isolated.HttpTrigger.3.x",
            archive, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))), evaluated.TargetFramework, evaluated.RootNamespace);
        SnapshotBinding binding = new(new NativeMsBuildBindingSnapshotSource(snapshot));
        using DefaultTemplateEngineHost host = new("func", "1.0.0", null,
            [.. Components.AllComponents, .. Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents,
                (typeof(IBindSymbolSource), binding)]);
        using EngineEnvironmentSettings settings = new(host, settingsLocation: Path.Combine(_root, "hive"));
        TemplatePackageManager manager = new(settings);
        IManagedTemplatePackageProvider provider = manager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> installed = await provider.InstallAsync([new InstallRequest(archive)], CancellationToken.None);
        installed.Should().ContainSingle().Which.Success.Should().BeTrue();
        ITemplateInfo template = (await manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == "Azure.Function.CSharp.Isolated.HttpTrigger.3.x");
        TemplateCreator creator = new(settings);

        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Greeting", "Greeting", _root,
            new Dictionary<string, string?> { ["namespace"] = evaluated.RootNamespace }, dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        binding.Reads.Should().Contain(("TargetFramework", "net10.0"));
        IPostAction package = preview.CreationEffects!.CreationResult.PostActions
            .Single(action => action.ActionId == new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814"));
        package.Args["reference"].Should().Be("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore");
        package.Args["version"].Should().Be("2.1.0");
        File.Exists(Path.Combine(_root, "Greeting.cs")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "obj", "project.assets.json")).Should().BeFalse();
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedUpstreamHttp_ActualEvaluatedSnapshot_UsesControlledSessionOrRefusesMissingBinding(bool missingSnapshot)
    {
        DotNetTemplateProjectContext evaluated = await ReadProjectAsync();
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(ExtractArchive(), hive);
        var directory = WorkingDirectory.FromExplicit(_root);
        using TemplateEngineSession session = new(new(directory, new(directory, "dotnet", "C#")), hive);
        using Templater resolver = TemplateFixtures.CreateTemplater(session);
        TemplateResolutionResult resolution = await resolver.ResolveGroupAsync(
            "Azure.Function.CSharp.Isolated.HttpTrigger.3.x", TemplateType.Item, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        ResolvedTemplate selected = resolution.Group![0];
        TemplateEffectSource source = new(selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!);
        IDotnetCliRunner runner = Substitute.For<IDotnetCliRunner>();
        SnapshotEffectCapability capability = new(new(runner), selected, source, evaluated.ProjectFilePath);
        NativeMsBuildBindingSnapshot snapshot = new(evaluated.ProjectFilePath, source.TemplateIdentity, source.Identity,
            source.Sha256, evaluated.TargetFramework, evaluated.RootNamespace);
        TemplateInvocationRequest request = new(directory,
            new Dictionary<string, string?> { ["namespace"] = evaluated.RootNamespace, ["AccessRights"] = "Anonymous" }, "Greeting")
        {
            EffectSupport = new(directory, evaluated.ProjectFilePath, capability, Confirmed: true),
            MsBuildBindings = missingSnapshot ? null : snapshot,
        };

        TemplateInvocationResult preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);

        if (missingSnapshot)
        {
            preview.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
            preview.Diagnostic.Should().Contain("explicit evaluated MSBuild snapshot");
            File.Exists(Path.Combine(_root, "Greeting.cs")).Should().BeFalse();
            runner.ReceivedCalls().Should().BeEmpty();
            return;
        }

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Plan.Effects.Should().ContainSingle(effect => effect is TemplatePackageReferenceEffect);
        File.Exists(Path.Combine(_root, "Greeting.cs")).Should().BeFalse();
        runner.ReceivedCalls().Should().BeEmpty();
        TemplateInvocationResult created = await selected.InvokeAsync(request, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Effects!.CompletedEffects.Should().HaveCount(2);
        File.ReadAllText(Path.Combine(_root, "Greeting.cs")).Should().Contain("namespace Actual.Functions", "AuthorizationLevel.Anonymous");
        await runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments.SequenceEqual(
            new[] { "add", evaluated.ProjectFilePath, "package", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "--version", "2.1.0" })),
            _root, CancellationToken.None);
        session.Settings.Components.OfType<IBindSymbolSource>().OfType<NativeMsBuildBindingSnapshotSource>().Should().ContainSingle();
    }

    private async Task<DotNetTemplateProjectContext> ReadProjectAsync()
    {
        string project = Path.Combine(_root, "Selected.csproj");
        File.WriteAllText(Path.Combine(_root, "project.props"),
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Actual.Functions</RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><Import Project="project.props" /></Project>""");
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        return await new DotNetTemplateProjectPreparation(new DotnetCliRunner(resolver)).ReadAsync(project, CancellationToken.None);
    }

    private string ExtractArchive()
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using ZipArchive outer = ZipFile.OpenRead(outerPath);
        outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        return archive;
    }

    private sealed class SnapshotEffectCapability(DotNetTemplateDependencyExecutor executor, ResolvedTemplate selected,
        TemplateEffectSource source, string project) : INativeTemplateEffectCapability
    {
        private readonly string _projectFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project)));

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            TemplateInvocationResult? changed = await selected.ValidateConfigurationAsync(cancellationToken);
            return changed is not null || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project))) != _projectFingerprint
                || plan.Effects.Any(effect => effect.Source != source
                    || (effect is not TemplateEditorNoOpEffect && effect.TargetPath != project))
                ? ["The exact selected source or evaluated project snapshot changed."] : [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            if (effect is not TemplatePackageReferenceEffect package || effect.Source != source || package.ProjectPath != project
                || await selected.ValidateConfigurationAsync(cancellationToken) is not null
                || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project))) != _projectFingerprint)
            {
                return new(false, ["The exact dependency effect is not qualified."]);
            }

            await executor.AddPackageReferenceAsync(project,
                new(package.PackageId, package.Version, package.ProjectFileExtension), cancellationToken);
            return new(true, []);
        }
    }

    private sealed class SnapshotBinding(NativeMsBuildBindingSnapshotSource source) : IBindSymbolSource
    {
        public string DisplayName => "Test-only evaluated .NET project snapshot";

        public string SourcePrefix => "msbuild";

        public int Priority => 200;

        public Guid Id => new("e534959b-4497-4a81-b9ef-430ee0a59237");

        public bool RequiresPrefixMatch => true;

        public List<(string Property, string? Value)> Reads { get; } = [];

        public async Task<string?> GetBoundValueAsync(IEngineEnvironmentSettings settings, string bindname, CancellationToken cancellationToken)
        {
            string? value = await source.GetBoundValueAsync(settings, bindname, cancellationToken);
            Reads.Add((bindname, value));
            return value;
        }
    }
}
