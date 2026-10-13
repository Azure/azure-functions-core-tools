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
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new([initializer]);
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        using Templater project = factory.CreateProject(Context("project"), new([]), planner, writer);
        using Templater ordinary = factory.Create(Context("ordinary"));
        var eligible = await project.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        var gated = await ordinary.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

        eligible.Status.Should().Be(TemplateResolutionStatus.Success);
        gated.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        project.Context.CommandDirectory.Should().NotBe(ordinary.Context.CommandDirectory);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("snapshot")]
    [InlineData("planner")]
    [InlineData("writer")]
    public void CreateProject_NullDependency_Throws(string argument)
    {
        ITemplaterFactory factory = new TemplaterFactory(new(Home));
        TemplateProjectConfigurationPlanner planner = new([]);
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        Action create = () => factory.CreateProject(argument == "context" ? null! : Context("project"),
            argument == "snapshot" ? null! : new([]), argument == "planner" ? null! : planner, argument == "writer" ? null! : writer);

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
