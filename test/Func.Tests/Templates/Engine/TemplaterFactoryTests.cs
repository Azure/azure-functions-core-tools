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
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([initializer]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        using Templater project = await factory.CreateProjectAsync(Context("project"), new([]), new(planner, writer), CancellationToken.None);
        using Templater ordinary = await factory.CreateAsync(Context("ordinary"), CancellationToken.None);
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
        ITemplaterFactory factory = Factory();
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(["Python"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        var context = new TemplateEngineContext(Context("composed").CommandDirectory,
            bundle: bundleVersion is null ? null : new("bundle", bundleVersion));
        using Templater templater = await factory.CreateProjectAsync(context,
            new([new("python-package", NuGetVersion.Parse("1.0"), ["python"], usable)]),
            new(planner, new(new PhysicalFileSystem())), CancellationToken.None);

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
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateProject_ProtectedFlow_PreservesPreviewConfigurationOwnershipAndLease(bool occupied)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), Path.Combine(Home, "templates"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "protected-project"));
        var configPath = CliConfigurationPathsOptions.GetProjectConfigPath(output.Info);
        const string userConfiguration = "{\"stack\":{\"runtime\":\"user-owned\"},\"custom\":true}";
        if (occupied)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, userConfiguration);
        }

        var hive = Path.Combine(Home, "templates");
        var before = Directory.GetFiles(hive, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(hive, path),
                path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        var store = new TemplateStore(new(Home), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        var context = new TemplateEngineContext(output, bundle: new("bundle", "4.35.0"));
        using var templater = await new TemplaterFactory(store).CreateProjectAsync(context, new([]), ProjectSupport(), CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource<TemplateStoreWaitReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingWriter = store.OpenWriteAsync(context, cancellation.Token, reason => waiting.TrySetResult(reason));
        try
        {
            (await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(TemplateStoreWaitReason.ActiveReaders);
            var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), DryRun: true, Force: true);

            var preview = await selected.InvokeAsync(request, CancellationToken.None);

            preview.Status.Should().Be(occupied ? TemplateInvocationStatus.FileConflict : TemplateInvocationStatus.Success);
            File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
            if (occupied)
            {
                File.ReadAllText(configPath).Should().Be(userConfiguration);
            }
            else
            {
                Directory.Exists(output.Info.FullName).Should().BeFalse();
            }

            var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

            created.Status.Should().Be(occupied ? TemplateInvocationStatus.FileConflict : TemplateInvocationStatus.Success);
            pendingWriter.IsCompleted.Should().BeFalse();
            if (occupied)
            {
                File.ReadAllText(configPath).Should().Be(userConfiguration);
                File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
                created.ConfiguredProjects.Should().BeEmpty();
            }
            else
            {
                using var configuration = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("python");
                configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("Python");
                created.ConfiguredProjects.Should().Equal(output.Info.FullName);
            }

            Directory.GetFiles(hive, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(hive, path),
                    path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
                .Should().BeEquivalentTo(before);
            templater.Dispose();
            using var writer = await pendingWriter.WaitAsync(TimeSpan.FromSeconds(10));
            Func<Task> stale = () => selected.InvokeAsync(request, CancellationToken.None);
            await stale.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [Fact]
    public async Task CreateProject_ForwardsPairedSupportContextAndCancellationToProtectedStore()
    {
        var store = Substitute.For<ITemplateStore>();
        var context = new TemplateEngineContext(Context("forwarding").CommandDirectory, bundle: new("bundle", "4.35.0"));
        var availability = new TemplateConstraintEvaluationSnapshot([]);
        var support = ProjectSupport();
        using var cancellation = new CancellationTokenSource();
        Action<TemplateStoreWaitReason> waiting = _ => { };
        using var engine = new TemplateEngineSession(context, Path.Combine(_root, "isolated"), availability, support);
        store.OpenReadAsync(context, cancellation.Token, availability, support, waiting).Returns(engine);

        using var templater = await new TemplaterFactory(store).CreateProjectAsync(context, availability, support, cancellation.Token, waiting);

        await store.Received(1).OpenReadAsync(context, cancellation.Token, availability, support, waiting);
        templater.Context.Should().BeSameAs(context);
        templater.Context.Bundle.Should().BeSameAs(context.Bundle);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("snapshot")]
    [InlineData("projectSupport")]
    public async Task CreateProject_NullDependency_Throws(string argument)
    {
        ITemplaterFactory factory = Factory();
        TemplateProjectConfigurationPlanner planner = new(new([]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        Func<Task> create = () => factory.CreateProjectAsync(argument == "context" ? null! : Context("project"),
            argument == "snapshot" ? null! : new([]), argument == "projectSupport" ? null! : new(planner, writer), CancellationToken.None);

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

    private static TemplateProjectSupport ProjectSupport()
    {
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(["Python"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["python"] = ["py"] });
        return new(new(new([stack])), new(new PhysicalFileSystem()));
    }

    private async Task InstallFixturesUnderHomeAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Path.Combine(Home, "templates"));
    }

    private TemplateEngineContext Context(string directory)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, directory)));
}
