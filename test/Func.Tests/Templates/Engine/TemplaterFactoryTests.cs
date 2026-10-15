// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-templater-factory-tests", Guid.NewGuid().ToString("N"));

    private string Home => Path.Combine(_root, "home");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp func home
        }
    }

    [Fact]
    public void Create_BindsTheSuppliedContext()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));
        TemplateEngineContext context = Context("command");

        using Templater templater = factory.Create(context);

        templater.Context.Should().BeSameAs(context);
    }

    [Fact]
    public async Task Create_ReadsTemplatesFromTemplatesDirectoryUnderFuncHome()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));

        using Templater templater = factory.Create(Context("command"));
        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().Contain(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task Create_EachCallReturnsAnIndependentTemplater()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));
        Templater first = factory.Create(Context("first"));
        using Templater second = factory.Create(Context("second"));

        first.Dispose();
        IReadOnlyList<TemplateCatalogEntry> entries = await second.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().Contain(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public void Create_NullContext_Throws()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));

        Action act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_SuppliedSnapshot_ControlsConstraintEligibility(bool usable)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), Path.Combine(Home, "templates"));
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], usable)]);

        using Templater templater = factory.Create(Context("snapshot"), snapshot);
        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(usable
            ? TemplateConstraintEvaluationStatus.Eligible
            : TemplateConstraintEvaluationStatus.Restricted);
    }

    [Fact]
    public void Create_SnapshotOverloadNullContext_Throws()
    {
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        Action action = () => factory.Create(null!, new([]));

        action.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void Create_NullSnapshot_Throws()
    {
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        Action action = () => factory.Create(Context("snapshot"), null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("snapshot");
    }

    [Fact]
    public async Task CreateProject_ExplicitSupport_EnablesOnlyTheProjectFlow()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), Path.Combine(Home, "templates"));
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([initializer]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        using Templater project = factory.CreateProject(Context("project"), new([]), new(planner, writer));
        using Templater ordinary = factory.Create(Context("ordinary"));
        var eligible = await project.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        var gated = await ordinary.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

        eligible.Status.Should().Be(TemplateResolutionStatus.Success);
        gated.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        project.Context.CommandDirectory.Should().NotBe(ordinary.Context.CommandDirectory);
    }

    [Theory]
    [InlineData(true, "4.20", true)]
    [InlineData(false, "4.20", false)]
    [InlineData(true, "4.10", false)]
    [InlineData(true, null, false)]
    public async Task CreateProject_WorkloadsAndBundle_KeepProjectAndItemEligibilitySeparate(bool usable, string? bundleVersion, bool allowed)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var path = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        configuration["constraints"]!["stack"] = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"func-workload","args":"python"}""");
        File.WriteAllText(path, configuration.ToJsonString());
        await TemplateFixtures.InstallAsync(package, Path.Combine(Home, "templates"));
        var itemPackage = TemplateFixtures.WriteJourneyPackage(_root);
        var itemPath = Path.Combine(itemPackage, "item", ".template.config", "template.json");
        var itemConfiguration = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(itemPath))!;
        itemConfiguration["constraints"]!["stack"]!["args"] = "python";
        itemConfiguration["constraints"]!["bundle"] = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"func-bundle","args":"[4.20,5.0)"}""");
        File.WriteAllText(itemPath, itemConfiguration.ToJsonString());
        await TemplateFixtures.InstallAsync(itemPackage, Path.Combine(Home, "templates"));
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(["Python"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        var context = new TemplateEngineContext(Context("composed").CommandDirectory,
            bundle: bundleVersion is null ? null : new("bundle", bundleVersion));
        using Templater templater = factory.CreateProject(context, new([new("python-package", NuGetVersion.Parse("1.0"), ["python"], usable)]),
            new(planner, new(new PhysicalFileSystem())));

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        var item = await templater.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None);

        item.Status.Should().Be(allowed ? TemplateResolutionStatus.Success : TemplateResolutionStatus.Unavailable);
        if (!allowed)
        {
            item.Group.Should().BeEmpty();
            item.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
        }

        result.Status.Should().Be(usable ? TemplateResolutionStatus.Success : TemplateResolutionStatus.Unavailable);
        if (usable)
        {
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "composed-project-output"));
            var created = await result.Group![0].InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

            created.Status.Should().Be(TemplateInvocationStatus.Success);
            created.ConfiguredProjects.Should().Equal(output.Info.FullName);
            File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
        }
        else
        {
            result.Group.Should().BeEmpty();
            result.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
        }
    }

    [Theory]
    [InlineData("context")]
    [InlineData("snapshot")]
    [InlineData("projectSupport")]
    public void CreateProject_NullDependency_Throws(string argument)
    {
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        TemplateProjectConfigurationPlanner planner = new(new([]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        Action create = () => factory.CreateProject(argument == "context" ? null! : Context("project"),
            argument == "snapshot" ? null! : new([]), argument == "projectSupport" ? null! : new(planner, writer));

        create.Should().Throw<ArgumentNullException>().WithParameterName(argument);
    }

    [Fact]
    public void Constructor_NullConfigurationPaths_Throws()
    {
        Action act = () => _ = new TemplaterFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configurationPaths");
    }

    private async Task InstallFixturesUnderHomeAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Path.Combine(Home, "templates"));
    }

    private TemplateEngineContext Context(string directory)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, directory)));
}
