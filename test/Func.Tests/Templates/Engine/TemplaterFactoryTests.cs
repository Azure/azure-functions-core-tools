// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using NuGet.Versioning;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task Create_BindsTheSuppliedContext()
    {
        TemplaterFactory factory = Factory();
        TemplateEngineContext context = Context("command");

        using Templater templater = await factory.CreateAsync(context, CancellationToken.None);

        templater.Context.Should().BeSameAs(context);
    }

    [Fact]
    public async Task Create_ReadsTemplatesFromTemplatesDirectoryUnderFuncHome()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = Factory();

        using Templater templater = await factory.CreateAsync(Context("command"), CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().Contain(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task Create_EachCallReturnsAnIndependentTemplater()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = Factory();
        Templater first = await factory.CreateAsync(Context("first"), CancellationToken.None);
        using Templater second = await factory.CreateAsync(Context("second"), CancellationToken.None);

        first.Dispose();
        IReadOnlyList<TemplateCatalogEntry> entries = await second.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().Contain(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task Create_NullContext_Throws()
    {
        TemplaterFactory factory = Factory();

        Func<Task> act = () => factory.CreateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_SuppliedSnapshot_ControlsConstraintEligibility(bool usable)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), Path.Combine(Home, "templates"));
        ITemplaterFactory factory = Factory();
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], usable)]);

        using Templater templater = await factory.CreateAsync(Context("snapshot"), snapshot, CancellationToken.None);
        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(usable
            ? TemplateConstraintEvaluationStatus.Eligible
            : TemplateConstraintEvaluationStatus.Restricted);
    }

    [Fact]
    public async Task Create_SnapshotOverloadNullContext_Throws()
    {
        ITemplaterFactory factory = Factory();
        Func<Task> action = () => factory.CreateAsync(null!, new([]), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task Create_NullSnapshot_Throws()
    {
        ITemplaterFactory factory = Factory();
        Func<Task> action = () => factory.CreateAsync(Context("snapshot"), null!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("snapshot");
    }

    [Fact]
    public async Task CreateProject_ExplicitSupport_EnablesOnlyTheProjectFlow()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), Path.Combine(Home, "templates"));
        ITemplaterFactory factory = Factory();
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new([initializer]);
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        using Templater project = await factory.CreateProjectAsync(Context("project"), new([]), planner, writer, CancellationToken.None);
        using Templater ordinary = await factory.CreateAsync(Context("ordinary"), CancellationToken.None);
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
    public async Task CreateProject_NullDependency_Throws(string argument)
    {
        ITemplaterFactory factory = Factory();
        TemplateProjectConfigurationPlanner planner = new([]);
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        Func<Task> create = () => factory.CreateProjectAsync(argument == "context" ? null! : Context("project"),
            argument == "snapshot" ? null! : new([]), argument == "planner" ? null! : planner, argument == "writer" ? null! : writer, CancellationToken.None);

        await create.Should().ThrowAsync<ArgumentNullException>().WithParameterName(argument);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Action act = () => _ = new TemplaterFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    private TemplaterFactory Factory()
        => new(new TemplateStore(new CliConfigurationPathsOptions(Home), new PhysicalFileSystem(), new FileTemplateStoreLock(),
            NullLoggerFactory.Instance));

    private async Task InstallFixturesUnderHomeAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Path.Combine(Home, "templates"));
    }

    private TemplateEngineContext Context(string directory)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, directory)));
}
