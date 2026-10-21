// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateProjectOutputKindTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-project-output-kind-" + Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

    [Theory]
    [InlineData("-.-", null, true)]
    [InlineData("-.-", null, false)]
    [InlineData("_._", null, true)]
    [InlineData("_._", null, false)]
    [InlineData("empty.directory", "empty.directory", true)]
    [InlineData("empty.directory", "empty.directory", false)]
    public async Task InvokeAsync_DirectoryHostWithGeneratedFileAnchor_IsRejectedBeforeCreation(
        string placeholder, string? configuredPlaceholder, bool dryRun)
    {
        var package = WritePackage(configuredPlaceholder, "package.json");
        var host = Path.Combine(package, "project", "host.json");
        File.Delete(host);
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, placeholder), string.Empty);
        await TemplateFixtures.InstallAsync(package, Hive);
        RecordingFileSystem fileSystem = new();
        using RecordingProjectSession session = CreateSession(fileSystem);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(
            TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "directory-host-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project", DryRun: dryRun), CancellationToken.None);

        AssertNativeDirectory(session, placeholder, "host.json", output);
        session.Preview!.FileChanges.Should().Contain(change =>
            Path.GetFileName(change.TargetRelativePath) == "package.json" && change.ChangeKind == ChangeKind.Create);
        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate,
            $"a directory cannot establish a host root; actual live calls {session.CreationCalls}, " +
            $"host directory {Directory.Exists(Path.Combine(output.Info.FullName, "host.json"))}, " +
            $"configuration file {File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json"))}");
        result.Diagnostic.Should().Be("Project creation must produce an active host.json root.");
        AssertNoCreation(session, fileSystem, result, output);
    }

    [Theory]
    [InlineData("-.-", null, true)]
    [InlineData("-.-", null, false)]
    [InlineData("_._", null, true)]
    [InlineData("_._", null, false)]
    [InlineData("empty.directory", "empty.directory", true)]
    [InlineData("empty.directory", "empty.directory", false)]
    public async Task InvokeAsync_DirectoryAnchorWithGeneratedFileHost_IsRejectedBeforeCreation(
        string placeholder, string? configuredPlaceholder, bool dryRun)
    {
        var package = WritePackage(configuredPlaceholder, "package.json");
        var anchor = Path.Combine(package, "project", "package.json");
        File.Delete(anchor);
        Directory.CreateDirectory(anchor);
        File.WriteAllText(Path.Combine(anchor, placeholder), string.Empty);
        await TemplateFixtures.InstallAsync(package, Hive);
        RecordingFileSystem fileSystem = new();
        using RecordingProjectSession session = CreateSession(fileSystem);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(
            TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "directory-anchor-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Project", DryRun: dryRun), CancellationToken.None);

        AssertNativeDirectory(session, placeholder, "package.json", output);
        session.Preview!.FileChanges.Should().Contain(change =>
            Path.GetFileName(change.TargetRelativePath) == "host.json" && change.ChangeKind == ChangeKind.Create);
        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate,
            $"a configuration anchor must be a generated file; actual live calls {session.CreationCalls}, " +
            $"host file {File.Exists(Path.Combine(output.Info.FullName, "host.json"))}, " +
            $"anchor directory {Directory.Exists(Path.Combine(output.Info.FullName, "package.json"))}");
        result.Diagnostic.Should().Be("Configuration references must bind to one resolved, generated primary output.");
        AssertNoCreation(session, fileSystem, result, output);
    }

    [Theory]
    [InlineData(null, "host.json")]
    [InlineData(null, "package.json")]
    [InlineData("empty.directory", "package.json")]
    [InlineData("empty.directory", "-.-")]
    public async Task InvokeAsync_FileHostAndFileAnchor_PreservePreviewNoOpAndCreation(
        string? configuredPlaceholder, string anchor)
    {
        var package = WritePackage(configuredPlaceholder, anchor);
        Directory.CreateDirectory(Path.Combine(package, "project", "empty"));
        File.WriteAllText(Path.Combine(package, "project", "empty", configuredPlaceholder ?? "-.-"), string.Empty);
        await TemplateFixtures.InstallAsync(package, Hive);
        RecordingFileSystem fileSystem = new();
        using RecordingProjectSession session = CreateSession(fileSystem);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(
            TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "valid-file-output"));
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Project", DryRun: true);

        var first = await selected.InvokeAsync(request, CancellationToken.None);
        var repeated = await selected.InvokeAsync(request, CancellationToken.None);

        first.Status.Should().Be(TemplateInvocationStatus.Success);
        repeated.Should().BeEquivalentTo(first);
        session.CreationCalls.Should().Be(0);
        fileSystem.ConfigurationCommits.Should().Be(0);
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        created.ConfiguredProjects.Should().Equal(output.Info.FullName);
        session.CreationCalls.Should().Be(1);
        fileSystem.ConfigurationCommits.Should().Be(1);
        File.ReadAllText(Path.Combine(output.Info.FullName, "host.json")).Should().Be("{\"version\":\"2.0\"}");
        File.Exists(Path.Combine(output.Info.FullName, anchor)).Should().BeTrue();
        Directory.Exists(Path.Combine(output.Info.FullName, anchor)).Should().BeFalse();
        Directory.Exists(Path.Combine(output.Info.FullName, "empty")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WritePackage(string? configuredPlaceholder, string anchor)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var path = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        configuration["primaryOutputs"] = new JsonArray(new JsonObject { ["path"] = anchor });
        if (configuredPlaceholder is not null)
        {
            configuration["placeholderFilename"] = configuredPlaceholder;
        }

        File.WriteAllText(path, configuration.ToJsonString());
        if (anchor != "host.json")
        {
            File.WriteAllText(Path.Combine(package, "project", anchor), "{}");
        }

        return package;
    }

    private RecordingProjectSession CreateSession(RecordingFileSystem fileSystem)
    {
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(["Python"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        return new(new(WorkingDirectory.FromExplicit(_root)), Hive, new(planner, new(fileSystem)));
    }

    private static void AssertNativeDirectory(RecordingProjectSession session, string placeholder, string target, WorkingDirectory output)
    {
        session.Preview.Should().NotBeNull();
        var effect = session.Preview!.FileChanges.OfType<IFileChange2>().Should().ContainSingle(change =>
            Path.GetFileName(change.SourceRelativePath) == placeholder).Subject;
        effect.ChangeKind.Should().Be(ChangeKind.Create);
        Path.GetFullPath(Path.Combine(output.Info.FullName, effect.TargetRelativePath)).Should()
            .Be(Path.Combine(output.Info.FullName, target));
    }

    private static void AssertNoCreation(
        RecordingProjectSession session, RecordingFileSystem fileSystem, TemplateInvocationResult result, WorkingDirectory output)
    {
        session.CreationCalls.Should().Be(0);
        fileSystem.ConfigurationCommits.Should().Be(0);
        result.Changes.Should().BeEmpty();
        result.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    private sealed class RecordingProjectSession(TemplateEngineContext context, string hive, TemplateProjectSupport support)
        : TemplateEngineSession(context, hive, projectSupport: support)
    {
        public ICreationEffects? Preview { get; private set; }

        public int CreationCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(
            ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (!dryRun)
            {
                CreationCalls++;
            }

            var result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (dryRun)
            {
                Preview = result.CreationEffects;
            }

            return result;
        }
    }

    private sealed class RecordingFileSystem : PhysicalFileSystem
    {
        public int ConfigurationCommits { get; private set; }

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            ConfigurationCommits++;
        }
    }
}
