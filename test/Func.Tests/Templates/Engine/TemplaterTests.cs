// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Versioning;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public async Task NativeEngine_InstalledItem_PreviewIsReadOnlyAndCreationUsesSameTemplate()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var output = Path.Combine(_root, "generated");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(output)), _hive);
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        var selected = templates.Should().ContainSingle(template => template.Identity == TemplateFixtures.ItemTemplateIdentity).Subject;
        TemplateCreator creator = new(session.Settings);

        var preview = await creator.InstantiateAsync(selected, "Welcome", "Welcome", output, new Dictionary<string, string?>(), dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        var change = preview.CreationEffects!.FileChanges.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output, change.TargetRelativePath)).Should().Be(Path.Combine(output, "Welcome.txt"));
        Directory.Exists(output).Should().BeFalse();

        var created = await creator.InstantiateAsync(selected, "Welcome", "Welcome", output, new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success);
        File.ReadAllText(Path.Combine(output, "Welcome.txt")).Should().Be("Welcome content");
    }

    [Fact]
    public async Task ItemJourney_ListSelectPreviewCreate_UsesFuncOwnedPath()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using Templater templater = CreateTemplater(snapshot);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "journey-output"));
        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var resolution = await templater.CreateGroupAsync(entries, CancellationToken.None);
        var selected = resolution.Group!.Should().ContainSingle().Subject;
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["Greeting"] = "welcome" }, "Journey", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        selected.Entry.Should().BeSameAs(entries[0]);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Changes.Should().ContainSingle().Which.Path.Should().EndWith("Journey.txt");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Journey.txt")).Should().Be("Journey welcome");
    }

    [Theory]
    [InlineData("Python")]
    [InlineData("PYTHON")]
    public async Task ProjectJourney_ListSelectPreviewCreate_PersistsMandatoryCliConfiguration(string aliasKey)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project-journey-output"));
        using Templater templater = CreateProjectTemplater(aliasKey: aliasKey);
        var entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);
        var resolution = await templater.CreateGroupAsync(entries, CancellationToken.None);
        var selected = resolution.Group!.Should().ContainSingle().Subject;
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["Greeting"] = "welcome" }, "Project", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        selected.Entry.Should().BeSameAs(entries[0]);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Changes.Should().Contain(change => change.Path.EndsWith("config.json", StringComparison.Ordinal));
        preview.Changes.Should().Contain(change => change.Path.EndsWith("Project.txt", StringComparison.Ordinal));
        preview.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        created.ConfiguredProjects.Should().ContainSingle().Which.Should().Be(output.Info.FullName);
        created.Changes.Select(change => Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))).Should().BeEquivalentTo(
            Path.Combine(output.Info.FullName, "host.json"), Path.Combine(output.Info.FullName, "Project.txt"),
            Path.Combine(output.Info.FullName, ".func", "config.json"));
        created.Changes.Should().OnlyContain(change => change.Kind == "Create");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(output.Info.FullName, "Project.txt")).Should().Be("Project welcome");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(output.Info.FullName, ".func", "config.json")).Build();
        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);
        options.Runtime.Should().Be("python");
        options.Language.Should().Be("Python");
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("javascript")]
    public async Task ProjectJourney_NodeHttpFixture_GeneratesFunctionAndCanonicalConfiguration(string aliasKey)
    {
        var (output, preview, created, previewCreatedDirectory) = await CreateNodeProjectAsync(_root, CancellationToken.None, aliasKey);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        previewCreatedDirectory.Should().BeFalse();
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        created.ConfiguredProjects.Should().ContainSingle().Which.Should().Be(output.Info.FullName);
        var function = JsonNode.Parse(File.ReadAllText(Path.Combine(output.Info.FullName, "Hello", "function.json")))!;
        function["bindings"]!.AsArray().Should().Contain(binding => binding!["type"]!.GetValue<string>() == "httpTrigger");
        File.ReadAllText(Path.Combine(output.Info.FullName, "Hello", "index.js")).Should().Contain("Hello from GeneratedApp").And.NotContain("GREETING");
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(output.Info.FullName, "local.settings.json")))!;
        settings["Values"]!["AzureWebJobsStorage"]!.GetValue<string>().Should().Be("UseDevelopmentStorage=true");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(output.Info.FullName, ".func", "config.json")).Build();
        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);
        options.Runtime.Should().Be("node");
        options.Language.Should().Be("JavaScript");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExportNodeSmokeProjectAsync_BlankDestination_RejectsArgument(string? destination)
    {
        Func<Task> action = () => ExportNodeSmokeProjectAsync(destination!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("parentDirectory");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportNodeSmokeProjectAsync_NonemptyDestination_PreservesExistingContent(bool directory)
    {
        var destination = Path.Combine(_root, "export");
        Directory.CreateDirectory(destination);
        var sentinel = Path.Combine(destination, "sentinel");
        if (directory)
        {
            Directory.CreateDirectory(sentinel);
        }
        else
        {
            File.WriteAllText(sentinel, "user content");
        }

        Func<Task> action = () => ExportNodeSmokeProjectAsync(destination, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("parentDirectory");
        Directory.EnumerateFileSystemEntries(destination).Should().ContainSingle().Which.Should().Be(sentinel);
        if (!directory)
        {
            File.ReadAllText(sentinel).Should().Be("user content");
        }
    }

    [Fact]
    public async Task ExportNodeSmokeProjectAsync_PreCancelled_DoesNotWriteFixture()
    {
        var destination = Path.Combine(_root, "cancelled-export");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> action = () => ExportNodeSmokeProjectAsync(destination, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task FixtureInstallAsync_PreCancelled_DoesNotCreateHive()
    {
        var package = TemplateFixtures.WriteRunnableNodeProjectPackage(_root);
        var hive = Path.Combine(_root, "cancelled-install-hive");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> action = () => TemplateFixtures.InstallAsync(package, hive, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(hive).Should().BeFalse();
    }

    public static async Task<string> ExportNodeSmokeProjectAsync(string parentDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(parentDirectory) && Directory.EnumerateFileSystemEntries(parentDirectory).Any())
        {
            throw new ArgumentException("The smoke fixture requires an empty output directory.", nameof(parentDirectory));
        }

        var (output, preview, created, previewCreatedDirectory) = await CreateNodeProjectAsync(parentDirectory, cancellationToken);
        if (preview.Status != TemplateInvocationStatus.Success || created.Status != TemplateInvocationStatus.Success || previewCreatedDirectory)
        {
            throw new InvalidOperationException($"Smoke project generation failed: {created.Diagnostic ?? preview.Diagnostic}");
        }

        return output.Info.FullName;
    }

    private static async Task<(WorkingDirectory Output, TemplateInvocationResult Preview, TemplateInvocationResult Created, bool PreviewCreatedDirectory)> CreateNodeProjectAsync(
        string parentDirectory, CancellationToken cancellationToken, string aliasKey = "JavaScript")
    {
        string home = Path.Combine(parentDirectory, "hive");
        string hive = Path.Combine(home, "templates");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteRunnableNodeProjectPackage(parentDirectory), hive, cancellationToken);
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("node");
        stack.DisplayName.Returns("Node.js");
        stack.SupportedLanguages.Returns(["JavaScript"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { [aliasKey] = ["js"] });
        var output = WorkingDirectory.FromExplicit(Path.Combine(parentDirectory, "generated"));
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());
        ITemplaterFactory factory = new TemplaterFactory(new(home));
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using Templater templater = factory.CreateProject(new(output), snapshot, new(planner, writer));
        var resolution = await templater.ResolveGroupAsync("Func.Tests.RunnableNodeProject", TemplateType.Project, cancellationToken);
        var selected = resolution.Group!.Single();
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["Greeting"] = "Hello" }, "GeneratedApp", DryRun: true);
        var preview = await selected.InvokeAsync(request, cancellationToken);
        bool previewCreatedDirectory = Directory.Exists(output.Info.FullName);
        var created = await selected.InvokeAsync(request with { DryRun = false }, cancellationToken);
        return (output, preview, created, previewCreatedDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectJourney_MountedTypeChanges_RequiresReselection(bool beforeSelection)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.ProjectTemplateIdentity);
        using Templater templater = CreateProjectTemplater();
        var configurationPath = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = JsonNode.Parse(File.ReadAllText(configurationPath))!;
        var selected = beforeSelection ? null
            : (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        configuration["tags"]!["type"] = "item";
        File.WriteAllText(configurationPath, configuration.ToJsonString());
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-project-type-output"));

        if (beforeSelection)
        {
            var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

            result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
            result.Group.Should().BeEmpty();
            result.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("type");
        }
        else
        {
            var result = await selected!.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

            result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
            result.Changes.Should().BeEmpty();
            result.ConfiguredProjects.Should().BeEmpty();
        }

        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_ItemInvocation_RemainsItemOnlyWithoutConfigurationWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project-flow-item-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Item"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        result.Changes.Should().ContainSingle().Which.Path.Should().EndWith("Item.txt");
        result.ConfiguredProjects.Should().BeEmpty();
        File.ReadAllText(Path.Combine(output.Info.FullName, "Item.txt")).Should().Be("Item content");
        Directory.Exists(Path.Combine(output.Info.FullName, ".func")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_NativeCreationFailure_DoesNotReportPreviewOrWriteConfiguration()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var fileSystem = Substitute.For<IFileSystem>();
        var session = new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, CreationResultStatus.CreateFailed,
            failCreation: true, projectSupport: new(CreateProjectPlanner(), new(fileSystem)));
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project-native-failure-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
        result.Changes.Should().BeEmpty();
        result.ConfiguredProjects.Should().BeEmpty();
        fileSystem.Received(1).FileExists(Path.Combine(output.Info.FullName, ".func", "config.json"));
        fileSystem.Received(1).DirectoryExists(Path.Combine(output.Info.FullName, ".func", "config.json"));
        fileSystem.ReceivedCalls().Should().HaveCount(2);
        await fileSystem.DidNotReceive().CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("package.json", true)]
    [InlineData("other/package.json", false)]
    public async Task ProjectJourney_PrimaryOutputAnchor_MustBelongToIndependentHostRoot(string relativeAnchor, bool valid)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var configPath = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = JsonNode.Parse(File.ReadAllText(configPath))!;
        configuration["primaryOutputs"] = new JsonArray(new JsonObject { ["path"] = relativeAnchor });
        File.WriteAllText(configPath, configuration.ToJsonString());
        var anchorPath = Path.Combine(package, "project", relativeAnchor.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(anchorPath)!);
        File.WriteAllText(anchorPath, "{}");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateProjectTemplater();
        var resolution = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "root-anchor-output"));

        var result = await resolution.Group![0].InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(valid ? TemplateInvocationStatus.Success : TemplateInvocationStatus.InvalidTemplate);
        if (valid)
        {
            File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
            File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
            result.ConfiguredProjects.Should().ContainSingle().Which.Should().Be(output.Info.FullName);
        }
        else
        {
            Directory.Exists(output.Info.FullName).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ProjectJourney_DeclaredButUngeneratedHost_FailsBeforeCreation()
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var configPath = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = JsonNode.Parse(File.ReadAllText(configPath))!;
        configuration["primaryOutputs"] = new JsonArray(new JsonObject { ["path"] = "host.json" }, new JsonObject { ["path"] = "package.json" });
        configuration["postActions"]![0]!["args"]!["primaryOutputIndex"] = "1";
        File.WriteAllText(configPath, configuration.ToJsonString());
        File.Delete(Path.Combine(package, "project", "host.json"));
        File.WriteAllText(Path.Combine(package, "project", "package.json"), "{}");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "unproduced-host-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_ExistingHostOverwrite_RequiresForceAndFinalizesConfiguration()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing-produced-host-output"));
        Directory.CreateDirectory(output.Info.FullName);
        var host = Path.Combine(output.Info.FullName, "host.json");
        File.WriteAllText(host, "original");
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>());

        var refused = await selected.InvokeAsync(request, CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(host).Should().Be("original");
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { Force = true }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(host).Should().Be("{\"version\":\"2.0\"}");
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task ProjectJourney_UndeclaredHostRoot_FailsBeforeCreation()
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        Directory.CreateDirectory(Path.Combine(package, "project", "extra"));
        File.WriteAllText(Path.Combine(package, "project", "extra", "host.json"), "{}");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "missing-project-config-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Contain("exactly one");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_AuthoredCliConfiguration_FailsBeforeCreation()
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        Directory.CreateDirectory(Path.Combine(package, "project", ".func"));
        File.WriteAllText(Path.Combine(package, "project", ".func", "config.json"), "{}");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "authored-config-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Contain("CLI-owned");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(".func")]
    [InlineData(".func/config.json/extra.txt")]
    public async Task ProjectJourney_ConfigPathCollision_FailsBeforeCreation(string relativePath)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var path = Path.Combine(package, "project", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "collision");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "config-collision-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_ExistingFileAtConfigDirectory_FailsBeforeCreation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing-config-directory-file"));
        Directory.CreateDirectory(output.Info.FullName);
        File.WriteAllText(Path.Combine(output.Info.FullName, ".func"), "user-owned");

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Be("A template output directory ancestor is an existing file. Choose an output directory without file collisions.");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
        File.ReadAllText(Path.Combine(output.Info.FullName, ".func")).Should().Be("user-owned");
    }

    [Fact]
    public async Task ProjectJourney_MalformedUnicodeCandidate_PreservesHealthyCatalogEntry()
    {
        var healthy = TemplateFixtures.WriteProjectJourneyPackage(Path.Combine(_root, "healthy"));
        var malformed = TemplateFixtures.WriteProjectJourneyPackage(Path.Combine(_root, "malformed"));
        var path = Path.Combine(malformed, "project", ".template.config", "template.json");
        var configuration = File.ReadAllText(path).TrimEnd().Replace(TemplateFixtures.ProjectTemplateIdentity, "Func.Tests.MalformedProject", StringComparison.Ordinal);
        File.WriteAllText(path, configuration[..^1] + ",\"extra\":{\"\\uD800\":1}}");
        await TemplateFixtures.InstallAsync(healthy, _hive);
        await TemplateFixtures.InstallAsync(malformed, _hive);
        using Templater templater = CreateProjectTemplater();

        var entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Identity == TemplateFixtures.ProjectTemplateIdentity)
            .Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Eligible);
        entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.MalformedProject")
            .Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
    }

    [SymbolicLinkFact]
    public async Task ProjectJourney_LinkedConfigurationDirectory_FailsBeforeCreation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "linked-config-output"));
        var external = Path.Combine(_root, "external-config");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(external);
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, ".func"), external);

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Be("Template output uses a symbolic link or reparse point. Choose an unlinked output directory.");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
        Directory.GetFiles(external).Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectJourney_ExistingUserConfiguration_ForceDoesNotOverwriteIt()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "user-config-output"));
        var config = Path.Combine(output.Info.FullName, ".func", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "user-owned");

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), Force: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        result.Diagnostic.Should().Be("Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.");
        File.ReadAllText(config).Should().Be("user-owned");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_ConfigurationDestinationIsDirectory_FailsBeforeCreatingRoot()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using Templater templater = CreateProjectTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "occupied-config-directory"));
        var directory = Path.Combine(output.Info.FullName, ".func", "config.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "user.txt"), "user-owned");

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), Force: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        result.Diagnostic.Should().Be("Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.");
        File.ReadAllText(Path.Combine(directory, "user.txt")).Should().Be("user-owned");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeFalse();
        result.ConfiguredProjects.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProjectJourney_InjectedConfigurationAuthority_RefusesOccupiedDestinationWithoutCreatingRoot(bool file, bool directory)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var fileSystem = Substitute.For<IFileSystem>();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "injected-config-occupancy"));
        var configurationPath = Path.Combine(output.Info.FullName, ".func", "config.json");
        fileSystem.FileExists(configurationPath).Returns(file);
        fileSystem.DirectoryExists(configurationPath).Returns(directory);
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), Force: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        result.Diagnostic.Should().Be("Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.");
        result.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        await fileSystem.DidNotReceive().CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProjectJourney_ConfigurationAppearsAtCommit_PreservesUserBytesAndReportsOnlyGeneratedEffects()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        CommitBoundaryFileSystem fileSystem = new(path => File.WriteAllBytes(path, [0, 1, 255, 42]));
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "commit-conflict-project"));
        var configurationPath = Path.Combine(output.Info.FullName, ".func", "config.json");

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
        fileSystem.CommitFailure.Should().NotBeNull();
        result.Diagnostic.Should().Be($"Mandatory project configuration failed. Generated files were preserved: {fileSystem.CommitFailure!.Message}");
        result.ConfiguredProjects.Should().BeEmpty();
        result.Changes.Select(change => Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))).Should().BeEquivalentTo(
            Path.Combine(output.Info.FullName, "host.json"), Path.Combine(output.Info.FullName, "Project.txt"));
        File.ReadAllBytes(configurationPath).Should().Equal(0, 1, 255, 42);
        File.ReadAllText(Path.Combine(output.Info.FullName, "host.json")).Should().Be("{\"version\":\"2.0\"}");
        Directory.GetFiles(Path.GetDirectoryName(configurationPath)!).Should().Equal(configurationPath);
    }

    [Fact]
    public async Task ProjectJourney_NullPreviewEffects_ReturnsInvalidTemplateWithoutCreation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var session = new ControlledProjectSession(new(WorkingDirectory.FromExplicit(_root)), _hive,
            new(CreateProjectPlanner(), new(new PhysicalFileSystem()))) { OmitPreviewEffects = true };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "missing-preview-effects"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Be("Project preview did not produce a configuration plan.");
        result.Changes.Should().BeEmpty();
        session.CreationCalls.Should().Be(0);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_DeletedHostEffect_ReturnsInvalidTemplateWithoutCreation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var session = new ControlledProjectSession(new(WorkingDirectory.FromExplicit(_root)), _hive,
            new(CreateProjectPlanner(), new(new PhysicalFileSystem()))) { DeleteHostEffect = true };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "deleted-host-effect"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Be("Declared host.json outputs must correspond to generated files.");
        result.Changes.Should().BeEmpty();
        session.CreationCalls.Should().Be(0);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_OutputAncestorBecomesFileAfterCreation_ReportsConfigurationFailureWithoutWriting()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "finalization-ancestor-file"));
        var session = new ControlledProjectSession(new(WorkingDirectory.FromExplicit(_root)), _hive,
            new(CreateProjectPlanner(), new(new PhysicalFileSystem())))
        {
            AfterCreation = () => File.WriteAllText(Path.Combine(output.Info.FullName, ".func"), "user-owned"),
        };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
        result.Diagnostic.Should().Be("A template output directory ancestor is an existing file. Choose an output directory without file collisions.");
        result.ConfiguredProjects.Should().BeEmpty();
        result.Changes.Should().NotContain(change => change.Path.EndsWith("config.json", StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(output.Info.FullName, ".func")).Should().Be("user-owned");
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ItemOutputAncestorIsFile_RejectsBeforeWriting()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item");
        Directory.CreateDirectory(Path.Combine(item, "nested"));
        File.Move(Path.Combine(item, "ItemFunction.txt"), Path.Combine(item, "nested", "ItemFunction.txt"));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "item-ancestor-file"));
        Directory.CreateDirectory(output.Info.FullName);
        File.WriteAllText(Path.Combine(output.Info.FullName, "nested"), "user-owned");

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Item"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Be("A template output directory ancestor is an existing file. Choose an output directory without file collisions.");
        result.Changes.Should().BeEmpty();
        File.ReadAllText(Path.Combine(output.Info.FullName, "nested")).Should().Be("user-owned");
        Directory.GetFiles(output.Info.FullName).Should().Equal(Path.Combine(output.Info.FullName, "nested"));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("unauthorized")]
    [InlineData("state")]
    public async Task ProjectJourney_ConfigurationWriteFailure_PreservesGeneratedFilesAndReportsFailure(string failure)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => Path.GetFileName((string)call[0]) == "host.json");
        Exception exception = failure switch
        {
            "unauthorized" => new UnauthorizedAccessException("controlled atomic write failure"),
            "state" => new InvalidOperationException("controlled atomic write failure"),
            _ => new IOException("controlled atomic write failure"),
        };
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(exception));
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "failed-config-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
        result.Diagnostic.Should().Contain("preserved");
        result.ConfiguredProjects.Should().BeEmpty();
        result.Changes.Should().NotBeEmpty().And.NotContain(change => change.Path.EndsWith("config.json", StringComparison.Ordinal));
        result.Changes.Should().OnlyContain(change => File.Exists(Path.Combine(output.Info.FullName, change.Path)));
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_FinalConfigurationCommitsBeforeCancellation_ReturnsCompletedSuccess()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommitFileSystem fileSystem = new(cancellation, cancelOnCommit: 1);
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "commit-then-cancel-output"));

        TemplateInvocationResult? result = null;
        var failure = await Record.ExceptionAsync(async () =>
            result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project"), cancellation.Token));

        cancellation.IsCancellationRequested.Should().BeTrue();
        fileSystem.CommittedConfigurations.Should().Equal(Path.Combine(output.Info.FullName, ".func", "config.json"));
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
        failure.Should().BeNull("the final configuration was physically committed before cancellation");
        result.Should().NotBeNull();
        result!.Status.Should().Be(TemplateInvocationStatus.Success);
        result.ConfiguredProjects.Should().Equal(output.Info.FullName);
        result.Changes.Select(change => Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))).Should().BeEquivalentTo(
            Path.Combine(output.Info.FullName, "host.json"), Path.Combine(output.Info.FullName, "Project.txt"),
            Path.Combine(output.Info.FullName, ".func", "config.json"));
        result.Changes.Should().OnlyContain(change => change.Kind == "Create");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(output.Info.FullName, ".func", "config.json")).Build();
        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);
        options.Runtime.Should().Be("python");
        options.Language.Should().Be("Python");
    }

    [Fact]
    public async Task ProjectJourney_LastOfTwoConfigurationsCommitsBeforeCancellation_ReportsBothCompletedRoots()
    {
        await TemplateFixtures.InstallAsync(WriteTwoRootProjectPackage(), _hive);
        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommitFileSystem fileSystem = new(cancellation, cancelOnCommit: 2);
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "last-commit-then-cancel-output"));
        var firstConfiguration = Path.Combine(output.Info.FullName, ".func", "config.json");
        var secondConfiguration = Path.Combine(output.Info.FullName, "second", ".func", "config.json");

        TemplateInvocationResult? result = null;
        var failure = await Record.ExceptionAsync(async () =>
            result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project"), cancellation.Token));

        cancellation.IsCancellationRequested.Should().BeTrue();
        fileSystem.CommittedConfigurations.Should().Equal(firstConfiguration, secondConfiguration);
        File.Exists(firstConfiguration).Should().BeTrue();
        File.Exists(secondConfiguration).Should().BeTrue();
        failure.Should().BeNull("both configurations were physically committed before cancellation");
        result.Should().NotBeNull();
        result!.Status.Should().Be(TemplateInvocationStatus.Success);
        result.ConfiguredProjects.Should().Equal(output.Info.FullName, Path.Combine(output.Info.FullName, "second"));
        result.Changes.Select(change => Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))).Should().BeEquivalentTo(
            Path.Combine(output.Info.FullName, "host.json"), Path.Combine(output.Info.FullName, "Project.txt"),
            Path.Combine(output.Info.FullName, "second", "host.json"), firstConfiguration, secondConfiguration);
        result.Changes.Should().OnlyContain(change => File.Exists(Path.Combine(output.Info.FullName, change.Path)));
    }

    [Fact]
    public async Task ProjectJourney_FirstConfigurationCommitsBeforeCancellation_DoesNotStartSecondPlan()
    {
        await TemplateFixtures.InstallAsync(WriteTwoRootProjectPackage(), _hive);
        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommitFileSystem fileSystem = new(cancellation, cancelOnCommit: 1);
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "partial-commit-then-cancel-output"));
        var firstConfiguration = Path.Combine(output.Info.FullName, ".func", "config.json");
        var secondConfiguration = Path.Combine(output.Info.FullName, "second", ".func", "config.json");

        Func<Task> invoke = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project"), cancellation.Token);

        var exception = (await invoke.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(cancellation.Token);
        fileSystem.CommittedConfigurations.Should().Equal(firstConfiguration);
        File.Exists(firstConfiguration).Should().BeTrue();
        File.Exists(secondConfiguration).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(secondConfiguration)).Should().BeFalse();
        File.ReadAllText(Path.Combine(output.Info.FullName, "host.json")).Should().Be("{\"version\":\"2.0\"}");
        File.ReadAllText(Path.Combine(output.Info.FullName, "second", "host.json")).Should().Be("{}");
    }

    [Fact]
    public async Task ProjectJourney_ConfigurationCanceledBeforePhysicalWrite_PropagatesCancellationWithoutConfiguration()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => Path.GetFileName((string)call[0]) == "host.json");
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await cancellation.CancelAsync();
            await new PhysicalFileSystem().CreateAllTextAsync((string)call[0], (string)call[1], (CancellationToken)call[2]);
        });
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-config-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>()), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(output.Info.FullName, ".func")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectJourney_ConfigurationFaultsAfterCancellation_PropagatesCancellation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectJourneyPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => Path.GetFileName((string)call[0]) == "host.json");
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await cancellation.CancelAsync();
            throw new IOException("controlled write failure after cancellation");
        });
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-fault-config-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>()), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(output.Info.FullName, "host.json")).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectJourney_TwoRoots_FinalizesInOrderAndPreservesPartialCompletion(bool failSecond)
    {
        var package = WriteTwoRootProjectPackage();
        await TemplateFixtures.InstallAsync(package, _hive);
        PhysicalFileSystem physical = new();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists((string)call[0]));
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            failSecond && ((string)call[0]).Contains($"second{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? Task.FromException(new IOException("second project finalization failed"))
                : physical.CreateAllTextAsync((string)call[0], (string)call[1], (CancellationToken)call[2]));
        using Templater templater = CreateProjectTemplater(fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "two-project-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(failSecond ? TemplateInvocationStatus.ConfigurationFailure : TemplateInvocationStatus.Success);
        IReadOnlyList<string> expectedProjects = failSecond ? [output.Info.FullName] : [output.Info.FullName, Path.Combine(output.Info.FullName, "second")];
        result.ConfiguredProjects.Should().Equal(expectedProjects);
        result.Changes.Where(change => change.Path.EndsWith("config.json", StringComparison.Ordinal))
            .Select(change => Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)))))
            .Should().Equal(expectedProjects);
        result.Changes.Should().OnlyContain(change => File.Exists(Path.Combine(output.Info.FullName, change.Path)));
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "second", ".func", "config.json")).Should().Be(!failSecond);
        File.Exists(Path.Combine(output.Info.FullName, "second", "host.json")).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveGroupAsync_RestrictedTemplate_DoesNotExposeInvocableItem()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync("FUNC-TESTS-JOURNEY", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
    }

    [Fact]
    public async Task ResolveGroupAsync_WrongType_DoesNotExposeGroup()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.WrongType);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task ResolveGroupAsync_SharedShortNameAcrossGroups_ReportsAmbiguity()
    {
        var first = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "first"));
        var second = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "second"));
        var config = Path.Combine(second, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace(TemplateFixtures.ItemTemplateIdentity, "Func.Tests.OtherItem", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(first, _hive);
        await TemplateFixtures.InstallAsync(second, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.AmbiguousGroup);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_RawDuplicateConstraintTypes_FailsBeforeResolution()
    {
        var package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":"node"}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"type\":\"func-workload\"", "\"type\":\"func-workload\",\"type\":\"func-workload\"", StringComparison.Ordinal));
        using Templater templater = CreateTemplater();

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var resolved = await templater.CreateGroupAsync(entries, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        resolved.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        resolved.Group.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ConfigurationChangedAfterSelection_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.AppendAllText(config, " ");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Changed"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        result.Diagnostic.Should().Contain("changed");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DisposedFlow_ThrowsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "disposed-output"));
        templater.Dispose();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Disposed"), CancellationToken.None);

        await action.Should().ThrowAsync<ObjectDisposedException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_CanceledFlow_ThrowsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-output"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Canceled"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnknownCanonicalParameter_FailsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-parameter-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["unknown"] = "value" }, "Invalid"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        result.Diagnostic.Should().Contain("canonical");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_FileConflict_RequiresExplicitForce()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "conflict-output"));
        Directory.CreateDirectory(output.Info.FullName);
        var target = Path.Combine(output.Info.FullName, "Conflict.txt");
        File.WriteAllText(target, "original");
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Conflict");

        var refused = await selected.InvokeAsync(request, CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        refused.Changes.Should().ContainSingle();
        File.ReadAllText(target).Should().Be("original");

        var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Changes.Should().ContainSingle().Which.Path.Should().EndWith("Conflict.txt");
        preview.Diagnostic.Should().Contain("requires explicit");
        File.ReadAllText(target).Should().Be("original");

        var forced = await selected.InvokeAsync(request with { Force = true }, CancellationToken.None);

        forced.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(target).Should().Be("Conflict content");
    }

    [Fact]
    public async Task InvokeAsync_UnsupportedPostAction_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"postActions\": [{\"actionId\":\"3A7C4B45-1F5D-4A30-959A-51B88E82B5D2\",\"description\":\"fixture\"}]", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "post-action-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Action"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("post-actions");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_SupplementalConfiguration_DoesNotHideUnvalidatedRequirements()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var configDirectory = Path.Combine(package, "item", ".template.config");
        var config = Path.Combine(configDirectory, "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"additionalConfigFiles\": [\"extra.template.json\"]", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(configDirectory, "extra.template.json"), """{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvedTemplate_ParameterMetadata_CannotBeMutated()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
        var selected = (await templater.ResolveGroupAsync("func-tests-journey", TemplateType.Item, CancellationToken.None)).Group![0];

        Action changeParameter = () => ((IList<TemplateParameterDefinition>)selected.Parameters)[0] = selected.Parameters[0] with { CanonicalName = "Changed" };

        changeParameter.Should().Throw<NotSupportedException>();
        selected.Parameters.Should().ContainSingle(parameter => parameter.CanonicalName == "Greeting");
    }

    [Fact]
    public async Task TemplateGroup_Filters_PreserveSelectedCapability()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
        var group = (await templater.ResolveGroupAsync("func-tests-journey", TemplateType.Item, CancellationToken.None)).Group!;

        var filtered = group.FilterByLanguage("csharp").FilterByArguments(new HashSet<string> { "Func.Tests.Journey" }).FilterToHighestPrecedence();

        filtered.Should().ContainSingle().Which.Should().BeSameAs(group[0]);
        group.Should().ContainSingle();
        group.FilterByLanguage("Python").Should().BeEmpty();
        group.FilterByArguments(new HashSet<string>()).FilterToHighestPrecedence().Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveGroupAsync_UnknownReference_ReportsNotFound()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync("missing", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.NotFound);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_DeletedConfiguration_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        File.Delete(Path.Combine(package, "item", ".template.config", "template.json"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "deleted-config-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Missing"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_LinkedOutputChild_RejectsExternalWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item");
        Directory.CreateDirectory(Path.Combine(item, "link"));
        File.Move(Path.Combine(item, "ItemFunction.txt"), Path.Combine(item, "link", "ItemFunction.txt"));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "linked-output"));
        var outside = Path.Combine(_root, "outside-output");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "link"), outside);

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Linked"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        File.Exists(Path.Combine(outside, "Linked.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvokeAsync_PermissionDeniedConfiguration_ReturnsFailureWithoutWrites(int deniedRead)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "denied-config-output"));
        session.DeniedRead = deniedRead;

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Denied"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("Rerun selection");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_NuGetPackageTypes_EnforceHostSharing(bool shared)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteSharedJourneyPackage(_root, shared), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var result = await templater.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(shared ? TemplateResolutionStatus.Unavailable : TemplateResolutionStatus.Success);
        result.Group.Should().HaveCount(shared ? 0 : 1);
        result.Diagnostics.Should().HaveCount(shared ? 1 : 0);
        if (shared)
        {
            result.Diagnostics[0].ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        }
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_LinkIntroducedAfterPreview_RejectsExternalWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item");
        Directory.CreateDirectory(Path.Combine(item, "link"));
        File.Move(Path.Combine(item, "ItemFunction.txt"), Path.Combine(item, "link", "ItemFunction.txt"));
        await TemplateFixtures.InstallAsync(package, _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "late-linked-output"));
        var outside = Path.Combine(_root, "late-outside-output");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        var preparedLink = Path.Combine(_root, "prepared-link");
        Directory.CreateSymbolicLink(preparedLink, outside);
        session.AfterPreview = () => Directory.Move(preparedLink, Path.Combine(output.Info.FullName, "link"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "LateLinked"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        Directory.Exists(preparedLink).Should().BeFalse();
        File.Exists(Path.Combine(outside, "LateLinked.txt")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_LinkedOutputAncestor_PreservesCreation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var realParent = Path.Combine(_root, "real-parent");
        var linkedParent = Path.Combine(_root, "linked-parent");
        Directory.CreateDirectory(realParent);
        Directory.CreateSymbolicLink(linkedParent, realParent);
        var output = WorkingDirectory.FromExplicit(Path.Combine(linkedParent, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "LinkedParent"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(realParent, "output", "LinkedParent.txt")).Should().Be("LinkedParent content");
        var change = result.Changes.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)).Should().Be(Path.Combine(output.Info.FullName, "LinkedParent.txt"));
    }

    [Fact]
    public async Task ListAsync_MalformedMountedJson_PreservesHealthyCatalogEntries()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.ItemTemplateIdentity);
        File.WriteAllText(Path.Combine(package, "item", ".template.config", "template.json"), "{ invalid JSON");
        using Templater templater = CreateTemplater();

        var items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var projects = await templater.ListAsync(TemplateType.Project, CancellationToken.None);
        var resolution = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);

        items.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        items[0].ConstraintEvaluation.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("could not be loaded");
        projects.Should().ContainSingle().Which.Identity.Should().Be(TemplateFixtures.ProjectTemplateIdentity);
        projects[0].ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.NotEvaluated);
        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        resolution.Group.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_InvalidPackageTypeMetadata_PreservesHealthyCatalogEntries()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "healthy")), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteSharedJourneyPackage(Path.Combine(_root, "malformed"), packageTypeVersion: "invalid-version"), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible);
        entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.Journey" && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
    }

    [Fact]
    public async Task ResolveGroupAsync_MixedTypeShortName_PreservesWrongTypeDiagnostic()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var project = Path.Combine(package, "project", ".template.config", "template.json");
        File.WriteAllText(project, File.ReadAllText(project).Replace(TemplateFixtures.ProjectTemplateShortName, TemplateFixtures.ItemTemplateShortName, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Success);
        result.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        result.Diagnostics.Should().ContainSingle().Which.Type.Should().Be(TemplateType.Project);
        result.Group!.Diagnostics.Should().BeEquivalentTo(result.Diagnostics);
    }

    [Theory]
    [InlineData(TemplateFixtures.OtherTypeTemplateIdentity)]
    [InlineData(TemplateFixtures.UntypedTemplateIdentity)]
    public async Task ResolveGroupAsync_MalformedType_ReportsAuthoringDiagnostic(string identity)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.InvalidTemplateType);
        result.Diagnostic.Should().Contain(identity);
        result.Group.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_UnreadableCandidate_PreservesHealthyCatalogEntries(bool ioFailure)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive) { DeniedIdentity = "Func.Tests.Journey", DeniedWithIo = ioFailure };
        using Templater templater = new(session);

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible);
        entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.Journey" && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
    }

    [Fact]
    public async Task ResolveGroupAsync_MalformedIdentityBeforeValidAlias_ReportsAuthoringDiagnostic()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(item, File.ReadAllText(item).Replace(TemplateFixtures.ItemTemplateShortName, TemplateFixtures.OtherTypeTemplateIdentity, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.OtherTypeTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.InvalidTemplateType);
        result.Group.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveGroupAsync_RejectedGroups_DoNotCreateEligibleAmbiguity(bool allUnavailable)
    {
        var basic = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "basic-family"));
        if (allUnavailable)
        {
            var config = Path.Combine(basic, "item", ".template.config", "template.json");
            File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"constraints\":{\"stack\":{\"type\":\"func-workload\",\"args\":\"python\"}}", StringComparison.Ordinal));
        }

        var restricted = TemplateFixtures.WriteJourneyPackage(Path.Combine(_root, "restricted-family"));
        var restrictedConfig = Path.Combine(restricted, "item", ".template.config", "template.json");
        File.WriteAllText(restrictedConfig, File.ReadAllText(restrictedConfig).Replace("func-tests-journey", TemplateFixtures.ItemTemplateShortName, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(basic, _hive);
        await TemplateFixtures.InstallAsync(restricted, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(allUnavailable ? TemplateResolutionStatus.Unavailable : TemplateResolutionStatus.Success);
        result.Group.Should().HaveCount(allUnavailable ? 0 : 1);
        result.Diagnostics.Should().HaveCount(allUnavailable ? 2 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_MultipleItemsInArchive_ReadsPackageMetadataOnce(bool malformed)
    {
        var package = TemplateFixtures.WriteSharedJourneyPackage(_root, shared: false, packageTypeVersion: malformed ? "invalid-version" : null, multiple: true);
        await TemplateFixtures.InstallAsync(package, _hive);
        var session = new CountingPackageSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().HaveCount(2);
        session.PackageReads.Should().Be(1);
        if (malformed)
        {
            entries.Should().OnlyContain(entry => entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
        }

        var freshSession = new CountingPackageSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater fresh = new(freshSession);
        await fresh.ListAsync(TemplateType.Item, CancellationToken.None);
        freshSession.PackageReads.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_PermissiveFuncArgs_RemainEligible()
    {
        var package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":[/* fixture */"node",]}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Eligible);
    }

    [Fact]
    public async Task ResolveGroupAsync_CaseDifferentIdentity_DoesNotPreemptShortName()
    {
        var basic = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "identity-package"));
        var alias = TemplateFixtures.WriteJourneyPackage(Path.Combine(_root, "alias-package"));
        var aliasConfig = Path.Combine(alias, "item", ".template.config", "template.json");
        var reference = TemplateFixtures.ItemTemplateIdentity.ToLowerInvariant();
        File.WriteAllText(aliasConfig, File.ReadAllText(aliasConfig).Replace("func-tests-journey", reference, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(basic, _hive);
        await TemplateFixtures.InstallAsync(alias, _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var result = await templater.ResolveGroupAsync(reference, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Success);
        result.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be("Func.Tests.Journey");

        var exact = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);
        exact.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        var aliasCase = await templater.ResolveGroupAsync("FUNC-TESTS-ITEM", TemplateType.Item, CancellationToken.None);
        aliasCase.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task ResolveGroupAsync_ProjectInItemOnlyFlow_DoesNotGrantInvocation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
        var entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);
        var picked = await templater.CreateGroupAsync(entries, CancellationToken.None);
        picked.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        picked.Group.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, CreationResultStatus.Success)]
    [InlineData(false, CreationResultStatus.CreateFailed)]
    [InlineData(false, CreationResultStatus.Cancelled)]
    public async Task InvokeAsync_UncommittedEngineReturnAfterCancellation_PropagatesCancellation(bool cancelPreview, CreationResultStatus nativeStatus)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        var session = new CancelingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, cancellation, cancelPreview, nativeStatus);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "engine-canceled-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Canceled", DryRun: cancelPreview), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_NativePreviewCompletesAfterCancellation_DoesNotCreateFiles(bool dryRun)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        using Templater templater = new(new CancelingAfterNativeSuccessSession(new(WorkingDirectory.FromExplicit(_root)), _hive, cancellation, true));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-native-preview"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Preview", DryRun: dryRun), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_NativeCreationCompletesBeforeCancellation_PreservesSuccessfulEffects(bool noOutputs)
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        if (noOutputs)
        {
            var configuration = Path.Combine(package, "item", ".template.config", "template.json");
            File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
                "\"sourceName\": \"ItemFunction\"",
                """ "sourceName": "ItemFunction", "sources": [{"modifiers":[{"condition":"true","exclude":["ItemFunction.txt"]}]}] """,
                StringComparison.Ordinal));
        }

        await TemplateFixtures.InstallAsync(package, _hive);
        using CancellationTokenSource cancellation = new();
        var session = new CancelingAfterNativeSuccessSession(new(WorkingDirectory.FromExplicit(_root)), _hive, cancellation);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "completed-native-output"));
        TemplateInvocationResult? result = null;

        Func<Task> action = async () => result = await selected.InvokeAsync(
            new(output, new Dictionary<string, string?>(), "Created"), cancellation.Token);

        Exception? exception = await Record.ExceptionAsync(action);

        cancellation.IsCancellationRequested.Should().BeTrue();
        if (!noOutputs)
        {
            File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("Created content");
        }

        exception.Should().BeNull("successful native creation must retain its completed effects");
        result.Should().NotBeNull();
        result!.Status.Should().Be(TemplateInvocationStatus.Success);
        if (noOutputs)
        {
            result.Changes.Should().BeEmpty();
            Directory.EnumerateFiles(output.Info.FullName, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        else
        {
            var change = result.Changes.Should().ContainSingle().Subject;
            change.Kind.Should().Be("Create");
            Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)).Should().Be(Path.Combine(output.Info.FullName, "Created.txt"));
        }
    }

    [Fact]
    public async Task InvokeAsync_PreCanceledUnknownParameter_PropagatesCancellation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-unknown-output"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["unknown"] = "value" }, "Canceled"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(CreationResultStatus.InvalidParamValues, "InvalidArguments")]
    [InlineData(CreationResultStatus.MissingMandatoryParam, "MissingRequiredParameters")]
    [InlineData(CreationResultStatus.CreateFailed, "EngineFailure")]
    [InlineData(CreationResultStatus.DestructiveChangesDetected, "FileConflict")]
    [InlineData(CreationResultStatus.NotFound, "TemplateUnavailable")]
    [InlineData(CreationResultStatus.TemplateIssueDetected, "InvalidTemplate")]
    [InlineData(CreationResultStatus.CondtionsEvaluationMismatch, "InvalidTemplate")]
    public async Task InvokeAsync_NativeFailure_PreservesStructuredCategory(CreationResultStatus nativeStatus, string expected)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var session = new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, nativeStatus);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native-failure-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Failure"), CancellationToken.None);

        result.Status.ToString().Should().Be(expected);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_PreflightFailures_PreserveStructuredCategories()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "typed-failure-output"));
        Directory.CreateDirectory(output.Info.FullName);
        File.WriteAllText(Path.Combine(output.Info.FullName, "Typed.txt"), "original");
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Typed");

        var invalid = await selected.InvokeAsync(request with { Parameters = new Dictionary<string, string?> { ["unknown"] = "value" } }, CancellationToken.None);
        var conflict = await selected.InvokeAsync(request, CancellationToken.None);
        File.AppendAllText(Path.Combine(package, "item", ".template.config", "template.json"), " ");
        var changed = await selected.InvokeAsync(request with { Force = true }, CancellationToken.None);

        invalid.Status.ToString().Should().Be("InvalidArguments");
        conflict.Status.ToString().Should().Be("FileConflict");
        changed.Status.ToString().Should().Be("TemplateChanged");
        File.ReadAllText(Path.Combine(output.Info.FullName, "Typed.txt")).Should().Be("original");
    }

    [Fact]
    public async Task InvokeAsync_NativeCancelledStatus_PropagatesCancellation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = new(new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, CreationResultStatus.Cancelled));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native-cancelled-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Cancelled"), CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_RealParameterValidation_PreservesInvalidAndMissingCategories()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"symbols\":{\"Mode\":{\"type\":\"parameter\",\"datatype\":\"choice\",\"isRequired\":true,\"choices\":[{\"choice\":\"safe\"}]}}", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-values-output"));

        var invalid = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["Mode"] = "unknown" }, "Invalid"), CancellationToken.None);
        var missing = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Missing"), CancellationToken.None);

        invalid.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        missing.Status.Should().Be(TemplateInvocationStatus.MissingRequiredParameters);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(CreationResultStatus.CreateFailed)]
    [InlineData(CreationResultStatus.Cancelled)]
    public async Task InvokeAsync_CreationFailureAfterSuccessfulPreview_PreservesCategory(CreationResultStatus nativeStatus)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = new(new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, nativeStatus, true));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "creation-failure-output"));
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Failure");

        if (nativeStatus == CreationResultStatus.Cancelled)
        {
            Func<Task> action = () => selected.InvokeAsync(request, CancellationToken.None);
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await selected.InvokeAsync(request, CancellationToken.None);
            result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
            result.Changes.Should().BeEmpty();
        }

        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task CreateGroupAsync_ForeignFlowEntry_RejectsUncheckedReplacement()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater first = CreateTemplater();
        using Templater second = CreateTemplater();
        var entries = await first.ListAsync(TemplateType.Item, CancellationToken.None);

        Func<Task> action = () => second.CreateGroupAsync(entries, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("entries");
    }

    [Fact]
    public async Task ListAsync_Item_ReturnsItemTemplateEntries()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new TemplateCatalogEntry(
            Identity: TemplateFixtures.ItemTemplateIdentity,
            Name: "Func Tests Item",
            Description: null,
            Type: TemplateType.Item,
            ShortNames: [TemplateFixtures.ItemTemplateShortName],
            GroupIdentity: null,
            Language: null,
            Precedence: 0,
            Package: new TemplatePackageInfo(packageDirectory, Version: null),
            IsHidden: false,
            Parameters: [],
            MetadataDiagnostic: null) { ConstraintEvaluation = TemplateConstraintEvaluation.Eligible });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_FuncWorkload_UsesCapturedUsability(bool usable)
    {
        string package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":"node"}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], usable)]);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, snapshot));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        var entry = entries.Should().ContainSingle().Subject;
        entry.ConstraintEvaluation.Status.Should().Be(usable ? TemplateConstraintEvaluationStatus.Eligible : TemplateConstraintEvaluationStatus.Restricted);
        entry.ConstraintEvaluation.Diagnostics.Should().HaveCount(usable ? 0 : 1);
    }

    [Fact]
    public async Task ListAsync_UnknownConstraint_RemainsVisibleAndUnavailable()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteConstraintPackage(_root, """{"future":{"type":"future-constraint","args":{}}}"""), _hive);
        using Templater templater = CreateTemplater();

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.NotEvaluated);
    }

    [Fact]
    public async Task ListAsync_Project_ReturnsProjectTemplateEntries()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        TemplateCatalogEntry entry = entries.Should().ContainSingle().Subject;
        entry.Identity.Should().Be(TemplateFixtures.ProjectTemplateIdentity);
        entry.Type.Should().Be(TemplateType.Project);
    }

    [Fact]
    public async Task ListAsync_TemplatesWithoutItemOrProjectType_AreNotListed()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);

        // The engine installs both, so only their type keeps them out of the catalog.
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.OtherTypeTemplateIdentity);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.UntypedTemplateIdentity);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> projects = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        items.Concat(projects).Select(entry => entry.Identity).Should().BeEquivalentTo(
            [TemplateFixtures.ItemTemplateIdentity, TemplateFixtures.ProjectTemplateIdentity]);
    }

    [Fact]
    public async Task ListAsync_FuncHostMetadata_AppliesParameterAliases()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.MetadataTemplateIdentity);
        entry.Language.Should().Be("TypeScript");
        entry.MetadataDiagnostic.Should().BeNull();
        entry.Parameters.Select(parameter => (parameter.CanonicalName, parameter.LongName, parameter.ShortName)).Should().Equal(
            ("Level", "log-level", "l"),
            ("Namespace", "Namespace", null));
    }

    [Fact]
    public async Task ListAsync_HiddenTemplate_IsListedAsHidden()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        Entry(entries, TemplateFixtures.HiddenTemplateIdentity).IsHidden.Should().BeTrue();
        Entry(entries, TemplateFixtures.DotnetHostOnlyTemplateIdentity).IsHidden
            .Should().BeFalse("dotnetcli.host.json only applies to dotnet new");
    }

    [Fact]
    public async Task ListAsync_InvalidFuncHostMetadata_ReportsDiagnosticOnEntry()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.InvalidHostTemplateIdentity);
        entry.MetadataDiagnostic.Should().Contain("func.host.json");
        entry.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_FromEngineCache_ReturnsSameEntriesAsFromScan()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        IReadOnlyList<TemplateCatalogEntry> scanned = await ListWithNewTemplaterAsync(TemplateType.Item);

        IReadOnlyList<TemplateCatalogEntry> cached = await ListWithNewTemplaterAsync(TemplateType.Item);

        scanned.Select(entry => entry.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.MetadataTemplateIdentity,
            TemplateFixtures.InvalidHostTemplateIdentity,
            TemplateFixtures.DotnetHostOnlyTemplateIdentity,
            TemplateFixtures.HiddenTemplateIdentity,
        ]);
        cached.Should().BeEquivalentTo(scanned);
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListMetadataTemplatesAsync()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        return await ListWithNewTemplaterAsync(TemplateType.Item);
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListWithNewTemplaterAsync(TemplateType type)
    {
        using Templater templater = CreateTemplater();
        return await templater.ListAsync(type, CancellationToken.None);
    }

    private static TemplateCatalogEntry Entry(IReadOnlyList<TemplateCatalogEntry> entries, string identity)
        => entries.Should().ContainSingle(candidate => candidate.Identity == identity).Subject;

    private Templater CreateTemplater(TemplateConstraintEvaluationSnapshot? snapshot = null)
        => new(new TemplateEngineSession(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive, snapshot));

    private Templater CreateProjectTemplater(IFileSystem? fileSystem = null, string aliasKey = "Python")
    {
        TemplateProjectConfigurationWriter writer = new(fileSystem ?? new PhysicalFileSystem());
        return new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive,
            projectSupport: new(CreateProjectPlanner(aliasKey), writer)));
    }

    private string WriteTwoRootProjectPackage()
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var path = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        configuration["primaryOutputs"]!.AsArray().Add(new JsonObject { ["path"] = "second/host.json" });
        var action = configuration["postActions"]!.AsArray()[0]!.DeepClone();
        action["args"]!["primaryOutputIndex"] = "1";
        configuration["postActions"]!.AsArray().Add(action);
        File.WriteAllText(path, configuration.ToJsonString());
        Directory.CreateDirectory(Path.Combine(package, "project", "second"));
        File.WriteAllText(Path.Combine(package, "project", "second", "host.json"), "{}");
        return package;
    }

    private sealed class CancelAfterConfigurationCommitFileSystem(CancellationTokenSource cancellation, int cancelOnCommit) : PhysicalFileSystem
    {
        private readonly CancellationTokenSource _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        private readonly List<string> _committedConfigurations = [];

        public IReadOnlyList<string> CommittedConfigurations => _committedConfigurations.AsReadOnly();

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            _committedConfigurations.Add(destinationPath);
            if (_committedConfigurations.Count == cancelOnCommit)
            {
                _cancellation.Cancel();
            }
        }
    }

    private static TemplateProjectConfigurationPlanner CreateProjectPlanner(string aliasKey = "Python")
    {
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { [aliasKey] = ["py"] });
        return new(new([initializer]));
    }

    private sealed class ControlledProjectSession(TemplateEngineContext context, string hive, TemplateProjectSupport support)
        : TemplateEngineSession(context, hive, projectSupport: support)
    {
        public bool OmitPreviewEffects { get; init; }

        public bool DeleteHostEffect { get; init; }

        public Action? AfterCreation { get; init; }

        public int CreationCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            var native = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (!dryRun)
            {
                CreationCalls++;
                AfterCreation?.Invoke();
                return native;
            }

            if (OmitPreviewEffects)
            {
                var missing = Substitute.For<ITemplateCreationResult>();
                missing.Status.Returns(CreationResultStatus.Success);
                missing.CreationEffects.Returns((ICreationEffects)null!);
                return missing;
            }

            if (DeleteHostEffect)
            {
                var change = Substitute.For<IFileChange>();
                change.TargetRelativePath.Returns("host.json");
                change.ChangeKind.Returns(ChangeKind.Delete);
                var effects = Substitute.For<ICreationEffects>();
                effects.FileChanges.Returns([change]);
                effects.CreationResult.Returns(native.CreationEffects!.CreationResult);
                var deleted = Substitute.For<ITemplateCreationResult>();
                deleted.Status.Returns(CreationResultStatus.Success);
                deleted.CreationEffects.Returns(effects);
                return deleted;
            }

            return native;
        }
    }

    private sealed class FailingEngineSession(TemplateEngineContext context, string hive, CreationResultStatus nativeStatus,
        bool failCreation = false, TemplateProjectSupport? projectSupport = null)
        : TemplateEngineSession(context, hive, projectSupport: projectSupport)
    {
        protected override Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (dryRun && failCreation)
            {
                return base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            }

            var result = Substitute.For<ITemplateCreationResult>();
            result.Status.Returns(nativeStatus);
            return Task.FromResult(result);
        }
    }

    private sealed class CancelingEngineSession(TemplateEngineContext context, string hive, CancellationTokenSource cancellation, bool cancelPreview, CreationResultStatus nativeStatus) : TemplateEngineSession(context, hive)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (dryRun == cancelPreview)
            {
                await cancellation.CancelAsync();
                var result = Substitute.For<ITemplateCreationResult>();
                result.Status.Returns(nativeStatus);
                return result;
            }

            return await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
        }
    }

    private sealed class CancelingAfterNativeSuccessSession(TemplateEngineContext context, string hive, CancellationTokenSource cancellation, bool cancelPreview = false) : TemplateEngineSession(context, hive)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            ITemplateCreationResult result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (dryRun == cancelPreview && result.Status == CreationResultStatus.Success)
            {
                await cancellation.CancelAsync();
            }

            return result;
        }
    }

    private sealed class CountingPackageSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        public int PackageReads { get; private set; }

        public override Task<IReadOnlyCollection<string>> ReadPackageTypesAsync(ITemplatePackage? package, CancellationToken cancellationToken)
        {
            PackageReads++;
            return base.ReadPackageTypesAsync(package, cancellationToken);
        }
    }

    private sealed class DenyingConfigurationSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        private int _readsAfterSelection;

        public int? DeniedRead { get; set; }

        public Action? AfterPreview { get; set; }

        public string? DeniedIdentity { get; set; }

        public bool DeniedWithIo { get; set; }

        public override Task<TemplateConfigurationSnapshot> ReadConfigurationAsync(ITemplateInfo template, CancellationToken cancellationToken)
        {
            if (template.Identity == DeniedIdentity)
            {
                if (DeniedWithIo)
                {
                    throw new IOException("Fixture candidate read failed.");
                }

                throw new UnauthorizedAccessException("Fixture candidate access denied.");
            }

            if (DeniedRead is not null || AfterPreview is not null)
            {
                _readsAfterSelection++;
                if (_readsAfterSelection == DeniedRead)
                {
                    throw new UnauthorizedAccessException("Fixture configuration access denied.");
                }

                if (_readsAfterSelection == 2)
                {
                    AfterPreview?.Invoke();
                }
            }

            return base.ReadConfigurationAsync(template, cancellationToken);
        }
    }
}
