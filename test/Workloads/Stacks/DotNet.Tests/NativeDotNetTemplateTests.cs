// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class NativeDotNetTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-dotnet-").FullName;
    private static readonly Guid _packageAction = new("B17581D1-C5C9-4489-8F0A-004BE667B814");
    private static readonly Guid _restoreAction = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    private static readonly Guid _editorAction = new("84C0DA21-51C8-4541-9940-6CA19AF04EE6");

    private static string OuterArchive => Directory.GetFiles(
        Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Pack_RealPhysicalWorkload_DeclaresExactNativeBytesAndCompleteRawInventory()
    {
        using var outer = new PackageArchiveReader(OuterArchive);
        outer.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncCliWorkload");
        using var declaration = JsonDocument.Parse(outer.GetStream("bundled-templates.json"));
        JsonElement asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Single();
        asset.GetProperty("path").GetString().Should().Be("tools/any/templates/dotnet.nupkg");
        asset.GetProperty("packageId").GetString().Should().Be("Azure.Functions.Templates.DotNet");
        using var content = new MemoryStream();
        outer.GetStream("tools/any/templates/dotnet.nupkg").CopyTo(content);
        Convert.ToHexStringLower(SHA256.HashData(content.ToArray())).Should().Be(asset.GetProperty("sha256").GetString());
        content.Position = 0;
        using var inner = new PackageArchiveReader(content);
        inner.NuspecReader.GetVersion().Should().Be(outer.NuspecReader.GetVersion());
        inner.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncTemplate");
        inner.GetFiles().Should().Contain("LICENSE", "provenance/upstream-sources.json",
            "provenance/Microsoft.Azure.Functions.Worker.ItemTemplates.nuspec.xml",
            "provenance/Microsoft.Azure.Functions.Worker.ProjectTemplates.nuspec.xml");
        JsonElement[] inventory = [.. asset.GetProperty("templates").EnumerateArray()];
        inventory.Should().HaveCount(38);
        inventory.Count(row => row.GetProperty("type").GetString() == "item").Should().Be(36);
        inventory.Count(row => row.GetProperty("type").GetString() == "project").Should().Be(2);
        inventory.Select(row => row.GetProperty("identity").GetString()).Should().OnlyHaveUniqueItems();
        inner.GetFiles().Count(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal)).Should().Be(38);
        foreach (JsonElement row in inventory)
        {
            using var configuration = JsonDocument.Parse(inner.GetStream(row.GetProperty("path").GetString()!));
            row.GetProperty("identity").GetString().Should().Be(configuration.RootElement.GetProperty("identity").GetString());
            row.GetProperty("type").GetString().Should().Be(configuration.RootElement.GetProperty("tags").GetProperty("type").GetString());
        }
    }

    [Theory]
    [InlineData("itemtemplates", "4.0.5590", "C1BBCC22B2B84F1A9EFFB88956E44BFC784A0ECC74DDE3F4204B6B4AD7128A64")]
    [InlineData("projecttemplates", "4.0.5337", "3BA17330B6C903BA159510337FA50942BD426AAF132F5DAC7B949B78AE8B8299")]
    public void Pack_VerifiedUpstreamArchive_PreservesEveryNativeContentByte(string kind, string version, string digest)
    {
        string upstreamPath = Path.Combine(AppContext.BaseDirectory, "upstream-dotnet-inputs",
            $"microsoft.azure.functions.worker.{kind}.{version}.nupkg");
        SHA256.HashData(File.ReadAllBytes(upstreamPath)).Should().Equal(Convert.FromHexString(digest));
        using var upstream = new ZipArchive(File.OpenRead(upstreamPath));
        using var inner = new PackageArchiveReader(ExtractInnerArchive());
        ZipArchiveEntry[] sourceFiles = [.. upstream.Entries.Where(entry => entry.FullName.StartsWith("content/", StringComparison.Ordinal)
            && entry.Name.Length > 0)];
        sourceFiles.Should().NotBeEmpty();
        foreach (ZipArchiveEntry source in sourceFiles)
        {
            using Stream expected = source.Open();
            using Stream actual = inner.GetStream(source.FullName);
            SHA256.HashData(actual).Should().Equal(SHA256.HashData(expected), source.FullName);
        }
    }

    [Fact]
    public async Task NativeCatalog_RealArchive_PreservesRawLanguageRecordsAndNativeParameters()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        templates.Should().HaveCount(38);
        ITemplateInfo[] items = [.. templates.Where(template => template.TagsCollection["type"] == "item")];
        items.Count(template => template.TagsCollection["language"] == "C#").Should().Be(29);
        items.Count(template => template.TagsCollection["language"] == "F#").Should().Be(7);
        items.Select(template => template.GroupIdentity).Distinct(StringComparer.Ordinal).Should().HaveCount(29);
        ITemplateInfo http = items.Single(template => template.Identity == "Azure.Function.CSharp.Isolated.HttpTrigger.3.x");
        http.ShortNameList.Should().Contain("http");
        ITemplateParameter access = http.ParameterDefinitions.Single(parameter => parameter.Name == "AccessRights");
        access.Choices!.Keys.Should().BeEquivalentTo("Function", "Anonymous", "Admin");
        access.DefaultValue.Should().Be("Function");
        http.ParameterDefinitions.Should().Contain(parameter => parameter.Name == "namespace");
        ITemplateInfo project = templates.Single(template => template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        project.ParameterDefinitions.Single(parameter => parameter.Name == "Framework").Choices!.Keys.Should().Contain("net10.0");
        (project as ITemplateInfoHostJsonCache)!.HostData.Should().Contain("target-framework", "tfm");
    }

    [Theory]
    [InlineData("CSharp", ".csproj", "Program.cs")]
    [InlineData("FSharp", ".fsproj", "Program.fs")]
    public async Task NativeProject_ExplicitSupportedFramework_RendersAndRetainsEvaluatedDependencyActions(
        string language, string extension, string program)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        string output = Path.Combine(_root, language);
        var parameters = new Dictionary<string, string?> { ["Framework"] = "net10.0" };
        TemplateCreator creator = new(session.Settings);

        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters, dryRun: true,
            cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        Directory.Exists(output).Should().BeFalse();
        IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
        actions.Should().Contain(action => action.ActionId == _restoreAction);
        actions.Should().OnlyContain(action => action.ActionId == _packageAction || action.ActionId == _restoreAction);
        if (language == "FSharp")
        {
            actions.Where(action => action.ActionId == _packageAction).Select(action => action.Args["reference"])
                .Should().Equal("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "Microsoft.Azure.Functions.Worker.Extensions.Http");
        }
        ITemplateCreationResult created = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "Sample" + extension)).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        File.Exists(Path.Combine(output, program)).Should().BeTrue();
        File.Exists(Path.Combine(output, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "local.settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, "_gitignore")).Should().BeFalse();
        File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse("raw native rendering is not CLI configuration finalization");
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("CSharp", ".csproj", ".cs", true)]
    [InlineData("FSharp", ".fsproj", ".fs", false)]
    public async Task NativeProjectAndHttp_ActualMsBuildCompileEvaluation_CharacterizesInheritedFSharpInclusionGap(
        string language, string projectExtension, string itemExtension, bool itemParticipates)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo project = templates.Single(template =>
            template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        ITemplateInfo http = templates.Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        string output = Path.Combine(_root, "compilation-" + language);
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult createdProject = await creator.InstantiateAsync(project, "Sample", "Sample", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: CancellationToken.None);
        createdProject.Status.Should().Be(CreationResultStatus.Success);
        string projectPath = Path.GetFullPath(Path.Combine(output, createdProject.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        Path.GetExtension(projectPath).Should().Be(projectExtension);
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateProjectContext evaluated = await new DotNetTemplateProjectPreparation(runner)
            .ReadAsync(projectPath, CancellationToken.None);
        evaluated.TargetFramework.Should().Be("net10.0");
        ITemplateCreationResult createdItem = await creator.InstantiateAsync(http, "Greeting", "Greeting", output,
            new Dictionary<string, string?> { ["namespace"] = evaluated.RootNamespace }, cancellationToken: CancellationToken.None);
        createdItem.Status.Should().Be(CreationResultStatus.Success);
        File.Exists(Path.Combine(output, "Greeting" + itemExtension)).Should().BeTrue();
        StringBuilder nativeOutput = new();

        await runner.RunStreamingAsync(["msbuild", projectPath, "-nologo", "-getItem:Compile"], output,
            line => nativeOutput.AppendLine(line), null, CancellationToken.None);

        using var items = JsonDocument.Parse(nativeOutput.ToString());
        string?[] compile = [.. items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())];
        compile.Contains("Greeting" + itemExtension, StringComparer.Ordinal).Should().Be(itemParticipates,
            "the independently exercised pinned dotnet-new baseline includes C# by glob but leaves the F# project with only Program.fs");
        if (!itemParticipates)
        {
            compile.Should().ContainSingle().Which.Should().Be("Program.fs",
                "this is a characterized gap, not evidence that the rendered F# HTTP function compiles");
        }

        File.Exists(Path.Combine(output, "obj", "project.assets.json")).Should().BeFalse("compile-item evaluation does not restore dependencies");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeFSharpHttp_QualifiedCompileRepair_BuildMetadataContainsGeneratedFunction()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo projectTemplate = templates.Single(template =>
            template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.FSharp.Isolated.3.x");
        ITemplateInfo itemTemplate = templates.Single(template => template.Identity == "Azure.Function.FSharp.Isolated.HttpTrigger.3.x");
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "repaired-native-fsharp");
        ITemplateCreationResult createdProject = await creator.InstantiateAsync(projectTemplate, "RepairedApp", "RepairedApp", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: CancellationToken.None);
        createdProject.Status.Should().Be(CreationResultStatus.Success);
        string project = Path.GetFullPath(Path.Combine(output, createdProject.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateProjectContext evaluated = await new DotNetTemplateProjectPreparation(runner).ReadAsync(project, CancellationToken.None);
        string qualification = Path.Combine(_root, "native-source-expansion");
        Dictionary<string, string?> arguments = new() { ["namespace"] = evaluated.RootNamespace, ["AccessRights"] = "Anonymous" };
        ITemplateCreationResult expanded = await creator.InstantiateAsync(itemTemplate, "RepairedHttp", "RepairedHttp", qualification,
            arguments, cancellationToken: CancellationToken.None);
        expanded.Status.Should().Be(CreationResultStatus.Success);
        string generated = Path.Combine(output, "RepairedHttp.fs");
        string expectedSource = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(qualification, "RepairedHttp.fs"))));
        FSharpCompileUpdater updater = new(runner, new());
        FSharpCompileUpdatePlan plan = await updater.PlanAsync(project, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project))),
            [Path.Combine(output, "Program.fs")], [new(generated, expectedSource)], CancellationToken.None);
        File.Exists(generated).Should().BeFalse();

        ITemplateCreationResult createdItem = await creator.InstantiateAsync(itemTemplate, "RepairedHttp", "RepairedHttp", output,
            arguments, cancellationToken: CancellationToken.None);
        createdItem.Status.Should().Be(CreationResultStatus.Success);
        await updater.ApplyAsync(plan, CancellationToken.None);

        StringBuilder evaluatedItems = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], output,
            line => evaluatedItems.AppendLine(line), null, CancellationToken.None);
        using var compile = JsonDocument.Parse(evaluatedItems.ToString());
        compile.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()).Should().Equal("RepairedHttp.fs", "Program.fs");
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("The repository NuGet.Config was not found.");
        }

        string configuration = Path.Combine(repository.FullName, "NuGet.Config");
        File.Copy(configuration, Path.Combine(output, "NuGet.Config"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        DotNetTemplateDependencyExecutor dependencies = new(runner);
        IPostAction package = createdItem.CreationEffects!.CreationResult.PostActions.Single(action => action.ActionId == _packageAction);
        await dependencies.AddPackageReferenceAsync(project,
            new(package.Args["reference"], package.Args["version"], package.Args["projectFileExtensions"]), timeout.Token);
        await dependencies.RestoreAsync(project, timeout.Token);
        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"], output, timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString())
            .Should().Contain("RepairedHttp", "the actual native F# HTTP file must participate in the built function metadata");
    }

    [Fact]
    public async Task NativeItems_AllRawConfigurations_RenderSourceAndRetainQualifiedActionShapes()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo[] items = [.. templates.Where(template => template.TagsCollection["type"] == "item")];
        items.Should().HaveCount(36);
        TemplateCreator creator = new(session.Settings);
        foreach (ITemplateInfo template in items)
        {
            string output = Path.Combine(_root, template.Identity);
            Dictionary<string, string?> parameters = new() { ["namespace"] = "Qualified.Functions" };
            ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Generated", "Generated", output,
                parameters, dryRun: true, cancellationToken: CancellationToken.None);
            preview.Status.Should().Be(CreationResultStatus.Success, $"{template.Identity}: {preview.ErrorMessage}");
            Directory.Exists(output).Should().BeFalse();
            IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
            actions.Should().NotBeEmpty(template.Identity);
            actions.Should().OnlyContain(action =>
                action.ActionId == _packageAction || action.ActionId == _restoreAction || action.ActionId == _editorAction, template.Identity);
            foreach (IPostAction action in actions.Where(action => action.ActionId == _packageAction))
            {
                action.Args["referenceType"].Should().Be("package");
                DotNetTemplatePackageReference reference = new(action.Args["reference"], action.Args["version"],
                    action.Args["projectFileExtensions"]);
                reference.ProjectFileExtension.Should().Be(template.TagsCollection["language"] == "F#" ? ".fsproj" : ".csproj");
            }

            ITemplateCreationResult created = await creator.InstantiateAsync(template, "Generated", "Generated", output,
                parameters, cancellationToken: CancellationToken.None);
            created.Status.Should().Be(CreationResultStatus.Success, $"{template.Identity}: {created.ErrorMessage}");
            string extension = template.TagsCollection["language"] == "F#" ? "*.fs" : "*.cs";
            Directory.EnumerateFiles(output, extension, SearchOption.AllDirectories).Should().NotBeEmpty(template.Identity);
            created.CreationEffects!.CreationResult.PostActions.Select(action => action.ActionId)
                .Should().Equal(actions.Select(action => action.ActionId), template.Identity);
        }
    }

    [Theory]
    [InlineData("CSharp", ".cs", ".csproj", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "2.1.0")]
    [InlineData("FSharp", ".fs", ".fsproj", "Microsoft.Azure.Functions.Worker.Extensions.Http", "3.3.0")]
    public async Task NativeHttp_ActualPreviewActions_ReachTypedPackageExecutorWithoutDroppingSourceOptions(
        string language, string extension, string projectExtension, string packageId, string version)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        string output = Path.Combine(_root, "http-" + language);
        var parameters = new Dictionary<string, string?> { ["namespace"] = "Sample.Functions", ["AccessRights"] = "Anonymous" };
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Greeting", "Greeting", output, parameters, dryRun: true,
            cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
        IPostAction package = actions.Should().ContainSingle(action => action.ActionId == _packageAction).Which;
        package.Args["referenceType"].Should().Be("package");
        package.Args["reference"].Should().Be(packageId);
        package.Args["version"].Should().Be(version);
        package.ContinueOnError.Should().BeTrue();
        actions.Should().Contain(action => action.ActionId == _editorAction && action.ContinueOnError);
        IDotnetCliRunner runner = Substitute.For<IDotnetCliRunner>();
        DotNetTemplateDependencyExecutor executor = new(runner);
        string project = Path.Combine(output, "Sample" + projectExtension);

        await executor.AddPackageReferenceAsync(project,
            new(package.Args["reference"], package.Args["version"], package.Args["projectFileExtensions"]), CancellationToken.None);

        await runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
            new[] { "add", project, "package", packageId, "--version", version })), output, CancellationToken.None);
        ITemplateCreationResult created = await creator.InstantiateAsync(template, "Greeting", "Greeting", output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "Greeting" + extension)).Should().Contain("Sample.Functions", "AuthorizationLevel.Anonymous");
    }

    private string ExtractInnerArchive()
    {
        string path = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using var outer = new PackageArchiveReader(OuterArchive);
        using FileStream target = File.Create(path);
        using Stream source = outer.GetStream("tools/any/templates/dotnet.nupkg");
        source.CopyTo(target);
        return path;
    }

    private async Task<NativeSession> OpenNativeSessionAsync()
    {
        NativeSession session = new(Path.Combine(_root, "hive"));
        IManagedTemplatePackageProvider provider = session.Manager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(ExtractInnerArchive())], CancellationToken.None);
        results.Should().ContainSingle().Which.Success.Should().BeTrue();
        return session;
    }

    private sealed class NativeSession : IDisposable
    {
        private readonly DefaultTemplateEngineHost _host = new("func", "1.0.0", null,
            [.. Components.AllComponents, .. Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents]);

        public NativeSession(string hive)
        {
            Settings = new(_host, settingsLocation: hive);
            Manager = new(Settings);
        }

        public EngineEnvironmentSettings Settings { get; }

        public TemplatePackageManager Manager { get; }

        public void Dispose()
        {
            Settings.Dispose();
            _host.Dispose();
        }
    }
}
