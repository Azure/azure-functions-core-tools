// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetBoundaryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-dotnet-boundary-").FullName;

    private string Hive => Path.Combine(_root, "hive");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("CSharp", "HttpTrigger.3.x", "access-rights", "AccessRights", "Anonymous")]
    [InlineData("FSharp", "HttpTrigger.3.x", "access-rights", "AccessRights", "Anonymous")]
    [InlineData("CSharp", "QueueTrigger.3.x", "queue-name", "QueueName", "orders")]
    [InlineData("FSharp", "QueueTrigger.3.x", "connection", "Connection", "QueueConnection")]
    [InlineData("FSharp", "CosmosDBTrigger.4.x", "database-name", "DatabaseName", "orders")]
    [InlineData("FSharp", "CosmosDBTrigger.4.x", "container-name", "ContainerName", "incoming")]
    public async Task NativeItemParameterAliases_OriginalAndCanonicalSpellings_ProduceTheSameCanonicalValues(
        string language, string variant, string originalName, string canonicalName, string value)
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.{variant}");
        IReadOnlyList<TemplateParameterDefinition> parameters = TemplateParameterProjector.Project(
            template.ParameterDefinitions, FuncHostTemplateMetadataReader.Read(template));
        TemplateArgumentParser parser = new();

        TemplateCandidateParseResult original = parser.Parse(new(template.Identity, parameters,
            new HashSet<string>(StringComparer.Ordinal), ["--" + originalName, value]), CancellationToken.None);
        TemplateCandidateParseResult canonical = parser.Parse(new(template.Identity, parameters,
            new HashSet<string>(StringComparer.Ordinal), ["--" + canonicalName, value]), CancellationToken.None);

        canonical.IsComplete.Should().BeTrue(string.Join(" ", canonical.InvalidInputDiagnostics.Select(diagnostic => diagnostic.Message)));
        original.IsComplete.Should().BeTrue(string.Join(" ", original.InvalidInputDiagnostics.Select(diagnostic => diagnostic.Message)));
        original.ValuesByCanonicalName.Should().BeEquivalentTo(canonical.ValuesByCanonicalName);
        original.ValuesByCanonicalName[canonicalName].Should().Be(value);
        original.ExplicitParameterNames.Should().BeEquivalentTo(canonicalName);
        canonical.ExplicitParameterNames.Should().BeEquivalentTo(canonicalName);
    }

    [Theory]
    [InlineData("--access-rights")]
    [InlineData("--AccessRights")]
    public async Task NativeItemParameterAliases_InvalidChoice_RejectsTheValueRatherThanTheOption(string alias)
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == "Azure.Function.CSharp.Isolated.HttpTrigger.3.x");
        IReadOnlyList<TemplateParameterDefinition> parameters = TemplateParameterProjector.Project(
            template.ParameterDefinitions, FuncHostTemplateMetadataReader.Read(template));

        TemplateCandidateParseResult result = new TemplateArgumentParser().Parse(new(template.Identity, parameters,
            new HashSet<string>(StringComparer.Ordinal), [alias, "__ParityInvalidChoice__"]), CancellationToken.None);

        result.DefinitionDiagnostics.Should().BeEmpty();
        TemplateArgumentDiagnostic diagnostic = result.InvalidInputDiagnostics.Should().ContainSingle().Subject;
        diagnostic.Message.Should().StartWith("Value '__ParityInvalidChoice__'").And.Contain("'Function', 'Anonymous', 'Admin'");
        diagnostic.TokenIndices.Should().Equal(0, 1);
    }

    [Fact]
    public async Task NativeItemParameterAliases_AllUserParameters_PreserveExistingHydratorSpellingsWithoutChangingCanonicalDefinitions()
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        TemplateOptionHydrator legacy = new([]);
        TemplateArgumentParser parser = new();
        int parameterCount = 0;
        foreach (ITemplateInfo template in (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Where(template => template.TagsCollection["type"] == "item"))
        {
            IReadOnlyList<TemplateParameterDefinition> parameters = TemplateParameterProjector.Project(
                template.ParameterDefinitions, FuncHostTemplateMetadataReader.Read(template));
            foreach (TemplateParameterDefinition parameter in parameters)
            {
                TemplateUserPrompt prompt = new(parameter.CanonicalName, parameter.Description, parameter.DataType,
                    parameter.DefaultValue, [.. parameter.Choices.Select(choice => choice.Value)], parameter.IsRequired, null, null, null);
                FunctionTemplateInfo oldShape = new(template.Identity, "dotnet", EngineIds.V2, template.Name, null, template.DefaultName,
                    [template.TagsCollection["language"]], new([prompt], false, null));
                string legacyAlias = legacy.Hydrate(oldShape).Single().Name;
                string value = parameter.Choices.FirstOrDefault()?.Value ?? "Public.AliasValue";
                TemplateCandidateParseResult canonical = parser.Parse(new(template.Identity, parameters,
                    new HashSet<string>(StringComparer.Ordinal), ["--" + parameter.CanonicalName, value]), CancellationToken.None);
                TemplateCandidateParseResult original = parser.Parse(new(template.Identity, parameters,
                    new HashSet<string>(StringComparer.Ordinal), [legacyAlias, value]), CancellationToken.None);

                canonical.IsComplete.Should().BeTrue(template.Identity + " " + parameter.CanonicalName);
                original.IsComplete.Should().BeTrue(template.Identity + " " + legacyAlias);
                original.ValuesByCanonicalName.Should().BeEquivalentTo(canonical.ValuesByCanonicalName);
                original.ExplicitParameterNames.Should().BeEquivalentTo(parameter.CanonicalName);
                parameterCount++;
            }
        }
        parameterCount.Should().Be(87);
    }

    [Theory]
    [InlineData("--database-name", "--DatabaseName")]
    [InlineData("--DatabaseName", "--database-name")]
    public async Task NativeItemParameterAliases_MixedSpellings_CannotSupplyTheSameCanonicalParameterTwice(string first, string second)
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == "Azure.Function.FSharp.Isolated.CosmosDBTrigger.4.x");
        IReadOnlyList<TemplateParameterDefinition> parameters = TemplateParameterProjector.Project(
            template.ParameterDefinitions, FuncHostTemplateMetadataReader.Read(template));

        TemplateCandidateParseResult result = new TemplateArgumentParser().Parse(new(template.Identity, parameters,
            new HashSet<string>(StringComparer.Ordinal), [first, "orders", second, "incoming"]), CancellationToken.None);

        result.DefinitionDiagnostics.Should().BeEmpty();
        TemplateArgumentDiagnostic diagnostic = result.InvalidInputDiagnostics.Should().ContainSingle(value =>
            value.Message.Contains("cannot be repeated", StringComparison.Ordinal)).Subject;
        diagnostic.TokenIndices.Should().Equal(0, 2);
        result.ValuesByCanonicalName.Should().NotContainKey("DatabaseName");
    }

    [Theory]
    [InlineData("CSharp", ".csproj")]
    [InlineData("FSharp", ".fsproj")]
    public async Task UnchangedUpstreamProject_NativePreviewResolvesName_QualifiedFuncInvocationStillRefusesTopology(
        string language, string extension)
    {
        await InstallArchiveAsync();
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("dotnet");
        stack.DisplayName.Returns(".NET");
        stack.WorkerRuntimeAliases.Returns(["dotnet-isolated"]);
        stack.SupportedLanguages.Returns(["C#", "F#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["C#"] = ["csharp"],
            ["F#"] = ["fsharp"],
        });
        TemplateProjectSupport support = new(new(new([stack])), new(new PhysicalFileSystem()));
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive, projectSupport: support);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string output = Path.Combine(_root, language);
        Dictionary<string, string?> parameters = new() { ["Framework"] = "net10.0" };
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters,
            dryRun: true, cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success);
        preview.CreationEffects!.CreationResult.PrimaryOutputs.Should().ContainSingle().Which.Path.Should().Be("Sample" + extension);

        TemplateInvocationResult refused = await session.InvokeAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(output), parameters, "Sample", DryRun: true), CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        refused.Diagnostic.Should().Be("Project topology property 'sources' is not qualified for this creation path.");
        refused.Changes.Should().BeEmpty();
        refused.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("FSharp")]
    public async Task NativeItem_WithoutExplicitActionCapability_RefusesBeforeMutation(string language)
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string output = Path.Combine(_root, language);

        TemplateInvocationResult refused = await session.InvokeAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(output), new Dictionary<string, string?>(), "Greeting"), CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.Failed);
        refused.Diagnostic.Should().Contain("post-actions");
        refused.Changes.Should().BeEmpty();
        Directory.Exists(output).Should().BeFalse();
    }

    private async Task InstallArchiveAsync()
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string innerPath = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(innerPath);
        }

        await TemplateFixtures.InstallAsync(innerPath, Hive);
    }
}
