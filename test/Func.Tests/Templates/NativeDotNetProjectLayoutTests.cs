// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetProjectLayoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-layout-").FullName;
    private readonly TemplateProjectConfigurationPlanner _planner = new(new([new DotNetProjectStack()]));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("CSharp", ".csproj", "C#", "Sample")]
    [InlineData("FSharp", ".fsproj", "F#", "Other.App")]
    public async Task QualifiedLayout_RealNativePreview_BindsGeneratedProjectAndCreatesCanonicalConfiguration(
        string language, string extension, string canonicalLanguage, string name)
    {
        using NativeProject source = await OpenProjectAsync(language);
        TemplateCreator creator = new(source.Session.Settings);
        Dictionary<string, string?> parameters = new() { ["Framework"] = "net10.0" };
        ITemplateCreationResult preview = await creator.InstantiateAsync(source.Template, name, name, source.Output, parameters,
            dryRun: true, cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success);

        IReadOnlyList<TemplateProjectConfigurationPlan> plans = _planner.CreateDotNetPlans(
            source.Configuration.Content, preview.CreationEffects!, source.Output, source.Qualification, parameters);

        TemplateProjectConfigurationPlan plan = plans.Should().ContainSingle().Subject;
        string nativeProject = preview.CreationEffects!.CreationResult.PrimaryOutputs.Should().ContainSingle().Subject.Path;
        plan.PrimaryOutput.Should().Be(Path.GetFullPath(Path.Combine(source.Output, nativeProject)));
        Path.GetExtension(plan.PrimaryOutput).Should().Be(extension);
        plan.Stack.Should().Be("dotnet");
        plan.Language.Should().Be(canonicalLanguage);
        Directory.Exists(source.Output).Should().BeFalse();
        ITemplateCreationResult created = await creator.InstantiateAsync(source.Template, name, name, source.Output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success);
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());
        await writer.WriteAsync(plan, CancellationToken.None);
        using var config = JsonDocument.Parse(File.ReadAllText(plan.ConfigurationPath));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("dotnet");
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(canonicalLanguage);
        File.ReadAllText(plan.PrimaryOutput).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        File.Exists(Path.Combine(source.Output, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(source.Output, "_gitignore")).Should().BeFalse();
        created.CreationEffects!.CreationResult.PostActions.Should().NotBeEmpty("layout qualification does not discard dependency actions");
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("FSharp")]
    public async Task DefaultPlanner_UnqualifiedUpstreamLayout_PreservesPriorRefusal(string language)
    {
        using NativeProject source = await OpenProjectAsync(language);
        Action action = () => _planner.Read(source.Configuration.Content);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*'sources'*");
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{},{}]")]
    [InlineData("[{\"modifiers\":[{\"rename\":{\"_gitignore\":\"../.gitignore\"}}]}]")]
    [InlineData("[{\"modifiers\":[{\"rename\":{\"_gitignore\":\".gitignore\",\"other\":\"file\"}}]}]")]
    [InlineData("[{\"modifiers\":[{\"condition\":\"true\",\"rename\":{\"_gitignore\":\".gitignore\"}}]}]")]
    [InlineData("[{\"source\":\"./\",\"modifiers\":[{\"rename\":{\"_gitignore\":\".gitignore\"}}]}]")]
    [InlineData("[{\"target\":\"child/\",\"modifiers\":[{\"rename\":{\"_gitignore\":\".gitignore\"}}]}]")]
    [InlineData("[{\"include\":[\"**/*\"],\"modifiers\":[{\"rename\":{\"_gitignore\":\".gitignore\"}}]}]")]
    [InlineData("[{\"exclude\":[\"host.json\"],\"modifiers\":[{\"rename\":{\"_gitignore\":\".gitignore\"}}]}]")]
    [InlineData("[{\"modifiers\":[{\"rename\":{\"*.txt\":\"*.cs\"}}]}]")]
    public async Task QualifiedLayout_UnsupportedSourceShape_RefusesWithoutGeneralizingSources(string sources)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        JsonObject configuration = JsonNode.Parse(source.Configuration.Content)!.AsObject();
        configuration["sources"] = JsonNode.Parse(sources);
        string candidate = configuration.ToJsonString();
        Action action = () => _planner.ReadDotNetLayout(candidate, Qualify(source.Template.Identity, candidate));

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*static _gitignore*");
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("sourceName")]
    [InlineData("symbolRename")]
    [InlineData("extraOutput")]
    [InlineData("conditionalOutput")]
    [InlineData("differentOutput")]
    [InlineData("outsideOutput")]
    [InlineData("customOperations")]
    [InlineData("supplemental")]
    [InlineData("frameworkSymbol")]
    public async Task QualifiedLayout_UnsupportedFilenameOrTopology_Refuses(string mutation)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        JsonObject root = JsonNode.Parse(source.Configuration.Content)!.AsObject();
        switch (mutation)
        {
            case "sourceName":
                root["sourceName"] = "Different";
                break;
            case "symbolRename":
                root["symbols"]!["Framework"]!["fileRename"] = "Project";
                break;
            case "extraOutput":
                root["primaryOutputs"]!.AsArray().Add(new JsonObject { ["path"] = "host.json" });
                break;
            case "conditionalOutput":
                root["primaryOutputs"]![0]!["condition"] = "true";
                break;
            case "differentOutput":
                root["primaryOutputs"]![0]!["path"] = "Fixed.csproj";
                break;
            case "outsideOutput":
                root["primaryOutputs"]![0]!["path"] = "../Company.FunctionApp.csproj";
                break;
            case "customOperations":
                root["globalCustomOperations"] = new JsonObject();
                break;
            case "supplemental":
                root["additionalConfigFiles"] = new JsonArray("other.json");
                break;
            case "frameworkSymbol":
                root["symbols"]!["Framework"]!["type"] = "computed";
                break;
        }

        string candidate = root.ToJsonString();
        Action action = () => _planner.ReadDotNetLayout(candidate, Qualify(source.Template.Identity, candidate));
        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("net8.0")]
    [InlineData("net48")]
    [InlineData("NET10.0")]
    public async Task QualifiedLayout_UnsupportedOrMissingFramework_DoesNotSupplyNewDefaults(string? framework)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        ITemplateCreationResult preview = await source.PreviewAsync();
        Dictionary<string, string?> parameters = framework is null ? [] : new() { ["Framework"] = framework };
        Action action = () => _planner.CreateDotNetPlans(source.Configuration.Content, preview.CreationEffects!,
            source.Output, source.Qualification, parameters);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*explicit Framework*net10.0*");
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("fingerprint")]
    public async Task QualifiedLayout_SelectionChanged_Refuses(string mismatch)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        DotNetProjectLayoutQualification qualification = new(
            mismatch == "identity" ? "Other.Template" : source.Template.Identity,
            mismatch == "fingerprint" ? new string('0', 64) : source.Configuration.Fingerprint);
        Action action = () => _planner.ReadDotNetLayout(source.Configuration.Content, qualification);

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*match*");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("host")]
    [InlineData("ignore")]
    public async Task QualifiedLayout_MissingGeneratedNativeEffect_Refuses(string removed)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        ITemplateCreationResult preview = await source.PreviewAsync();
        ICreationEffects effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(preview.CreationEffects!.CreationResult);
        effects.FileChanges.Returns([.. preview.CreationEffects.FileChanges.Where(change => removed switch
        {
            "project" => !change.TargetRelativePath.EndsWith(".csproj", StringComparison.Ordinal),
            "host" => !change.TargetRelativePath.EndsWith("host.json", StringComparison.Ordinal),
            _ => !change.TargetRelativePath.EndsWith(".gitignore", StringComparison.Ordinal),
        })]);
        Action action = () => _planner.CreateDotNetPlans(source.Configuration.Content, effects, source.Output,
            source.Qualification, new Dictionary<string, string?> { ["Framework"] = "net10.0" });

        action.Should().Throw<InvalidTemplateMetadataException>();
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside.csproj")]
    [InlineData("child/Sample.csproj")]
    [InlineData("Sample.fsproj")]
    [InlineData("Missing.csproj")]
    public async Task QualifiedLayout_ChangedNativePrimaryOutput_RefusesInsteadOfGuessingProject(string path)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        ITemplateCreationResult preview = await source.PreviewAsync();
        ICreationPath output = Substitute.For<ICreationPath>();
        output.Path.Returns(path);
        ICreationResult result = Substitute.For<ICreationResult>();
        result.PrimaryOutputs.Returns([output]);
        result.PostActions.Returns(preview.CreationEffects!.CreationResult.PostActions);
        ICreationEffects effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(result);
        effects.FileChanges.Returns(preview.CreationEffects.FileChanges);
        Action action = () => _planner.CreateDotNetPlans(source.Configuration.Content, effects, source.Output,
            source.Qualification, new Dictionary<string, string?> { ["Framework"] = "net10.0" });

        action.Should().Throw<InvalidTemplateMetadataException>();
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Theory]
    [InlineData("secondRoot")]
    [InlineData("outside")]
    [InlineData("configuration")]
    [InlineData("extraProject")]
    [InlineData("wrongSource")]
    public async Task QualifiedLayout_UnsupportedNativeFileEffects_Refuses(string corruption)
    {
        using NativeProject source = await OpenProjectAsync("CSharp");
        ITemplateCreationResult preview = await source.PreviewAsync();
        IFileChange2 change = Substitute.For<IFileChange2>();
        change.ChangeKind.Returns(ChangeKind.Create);
        change.SourceRelativePath.Returns(corruption == "wrongSource" ? "Other.csproj" : "extra.txt");
        change.TargetRelativePath.Returns(corruption switch
        {
            "secondRoot" => "child/host.json",
            "outside" => "../outside.txt",
            "configuration" => ".func/config.json",
            "extraProject" => "Other.csproj",
            _ => "Sample.csproj",
        });
        ICreationEffects effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(preview.CreationEffects!.CreationResult);
        effects.FileChanges.Returns(corruption == "wrongSource"
            ? [.. preview.CreationEffects.FileChanges.Where(file => !file.TargetRelativePath.EndsWith(".csproj", StringComparison.Ordinal)), change]
            : [.. preview.CreationEffects.FileChanges, change]);
        Action action = () => _planner.CreateDotNetPlans(source.Configuration.Content, effects, source.Output,
            source.Qualification, new Dictionary<string, string?> { ["Framework"] = "net10.0" });

        action.Should().Throw<InvalidTemplateMetadataException>();
        Directory.Exists(source.Output).Should().BeFalse();
    }

    [Fact]
    public async Task QualifiedLayout_ExistingConfigurationWriter_RefusesReplacementAndPreservesUserBytes()
    {
        using NativeProject source = await OpenProjectAsync("FSharp");
        ITemplateCreationResult preview = await source.PreviewAsync();
        TemplateProjectConfigurationPlan plan = _planner.CreateDotNetPlans(source.Configuration.Content, preview.CreationEffects!,
            source.Output, source.Qualification, new Dictionary<string, string?> { ["Framework"] = "net10.0" }).Single();
        Directory.CreateDirectory(Path.GetDirectoryName(plan.ConfigurationPath)!);
        File.WriteAllText(plan.PrimaryOutput, "generated project");
        File.WriteAllText(plan.ConfigurationPath, "user owned");
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());
        Func<Task> action = () => writer.WriteAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*user-owned*");
        File.ReadAllText(plan.ConfigurationPath).Should().Be("user owned");
    }

    [Theory]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Template", null)]
    [InlineData("Template", "")]
    [InlineData("Template", "short")]
    public void Qualification_InvalidArguments_Throws(string? identity, string? fingerprint)
    {
        Action action = () => new DotNetProjectLayoutQualification(identity!, fingerprint!);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Planner_NullOrBlankQualificationInputs_ThrowProgrammerErrors()
    {
        Action[] actions =
        [
            () => _planner.ReadDotNetLayout(null!, new("Template", new string('a', 64))),
            () => _planner.ReadDotNetLayout("{}", null!),
            () => _planner.CreateDotNetPlans("{}", null!, _root, new("Template", new string('a', 64)), new Dictionary<string, string?>()),
            () => _planner.CreateDotNetPlans("{}", Substitute.For<ICreationEffects>(), "", new("Template", new string('a', 64)), new Dictionary<string, string?>()),
            () => _planner.CreateDotNetPlans("{}", Substitute.For<ICreationEffects>(), _root, new("Template", new string('a', 64)), null!),
        ];

        foreach (Action action in actions)
        {
            action.Should().Throw<ArgumentException>();
        }
    }

    private static DotNetProjectLayoutQualification Qualify(string identity, string content)
        => new(identity, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    private async Task<NativeProject> OpenProjectAsync(string language)
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(archive, hive);
        TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        return new(session, template, configuration, Path.Combine(_root, "output"));
    }

    private sealed record NativeProject(TemplateEngineSession Session, ITemplateInfo Template,
        TemplateConfigurationSnapshot Configuration, string Output) : IDisposable
    {
        public DotNetProjectLayoutQualification Qualification => new(Template.Identity, Configuration.Fingerprint);

        public void Dispose() => Session.Dispose();

        public Task<ITemplateCreationResult> PreviewAsync() => new TemplateCreator(Session.Settings).InstantiateAsync(
            Template, "Sample", "Sample", Output, new Dictionary<string, string?> { ["Framework"] = "net10.0" },
            dryRun: true, cancellationToken: CancellationToken.None);
    }
}
