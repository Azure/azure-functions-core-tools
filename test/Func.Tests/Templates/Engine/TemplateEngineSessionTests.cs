// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateEngineSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-template-session-tests", Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

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
    public void Constructor_RegistersRunnableProjectsAndFuncConstraintComponents()
    {
        using TemplateEngineSession session = new(CommandContext("command"), Hive);

        // RunnableProjects generator components are registered, so the host can
        // load and render standard template.json packages.
        session.Settings.Components.OfType<IGenerator>().Should().NotBeEmpty();

        // Every member of the func constraint factory component set is
        // registered on the host under the engine interface it is resolved by
        // (e.g. ITemplateConstraintFactory). The component manager caches by
        // that exact interface, so it must be probed with the registered type,
        // not the IIdentifiedComponent base (which is never a cache key).
        MethodInfo tryGetComponent = typeof(IComponentManager)
            .GetMethod(nameof(IComponentManager.TryGetComponent))!;

        foreach ((Type Type, IIdentifiedComponent Instance) in FuncTemplateComponents.Create(new([])))
        {
            object?[] args = [Instance.Id, null];
            bool resolved = (bool)tryGetComponent
                .MakeGenericMethod(Type)
                .Invoke(session.Settings.Components, args)!;

            resolved.Should().BeTrue($"{Instance.GetType().Name} should be resolvable as {Type.Name}");
        }
    }

    [Fact]
    public async Task Constraints_SeparateSessionSnapshots_DoNotShareEligibility()
    {
        var usable = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        var installedOnly = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], false)]);
        using TemplateEngineSession node = new(CommandContext("node"), Hive, usable);
        using TemplateEngineSession diagnostic = new(CommandContext("diagnostic"), Hive, installedOnly);
        var nodeFactory = node.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var diagnosticFactory = diagnostic.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var nodeConstraint = await nodeFactory.CreateTemplateConstraintAsync(node.Settings, CancellationToken.None);
        var diagnosticConstraint = await diagnosticFactory.CreateTemplateConstraintAsync(diagnostic.Settings, CancellationToken.None);

        var nodeResult = nodeConstraint.Evaluate("\"node\"");
        var diagnosticResult = diagnosticConstraint.Evaluate("\"node\"");

        nodeResult.EvaluationStatus.Should().Be(TemplateConstraintResult.CreateAllowed(nodeConstraint).EvaluationStatus);
        diagnosticResult.EvaluationStatus.Should().Be(TemplateConstraintResult.CreateRestricted(diagnosticConstraint, "unavailable").EvaluationStatus);
        nodeFactory.Should().NotBeSameAs(diagnosticFactory);
    }

    [Theory]
    [InlineData("4.20", true)]
    [InlineData("4.10", false)]
    [InlineData(null, false)]
    public async Task Constraints_Bundle_UsesResolvedCommandContext(string? version, bool allowed)
    {
        var context = new TemplateEngineContext(DirectoryUnderRoot("bundle"), bundle: version is null ? null : new("bundle", version));
        using TemplateEngineSession session = new(context, Hive);
        var factory = session.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-bundle");
        var constraint = await factory.CreateTemplateConstraintAsync(session.Settings, CancellationToken.None);

        var result = constraint.Evaluate("\"[4.20,5.0)\"");

        result.EvaluationStatus.Should().Be(allowed
            ? TemplateConstraintResult.CreateAllowed(constraint).EvaluationStatus
            : TemplateConstraintResult.CreateRestricted(constraint, "unavailable").EvaluationStatus);
    }

    [Fact]
    public async Task Constraints_SharedWorkloadSnapshot_DoesNotOverrideSessionBundle()
    {
        var snapshot = new TemplateConstraintEvaluationSnapshot([]);
        using TemplateEngineSession resolved = new(new(DirectoryUnderRoot("bundle"), bundle: new("bundle", "4.20")), Hive, snapshot);
        using TemplateEngineSession missing = new(CommandContext("no-bundle"), Hive, snapshot);
        var resolvedFactory = resolved.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-bundle");
        var missingFactory = missing.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-bundle");
        var resolvedConstraint = await resolvedFactory.CreateTemplateConstraintAsync(resolved.Settings, CancellationToken.None);
        var missingConstraint = await missingFactory.CreateTemplateConstraintAsync(missing.Settings, CancellationToken.None);

        resolvedConstraint.Evaluate("\"[4.20,5.0)\"").EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Allowed);
        missingConstraint.Evaluate("\"[4.20,5.0)\"").EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Restricted);
    }

    [Fact]
    public async Task Constraints_MalformedOrMissingArguments_FailClosed()
    {
        using TemplateEngineSession session = new(CommandContext("invalid"), Hive);
        var factory = session.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var constraint = await factory.CreateTemplateConstraintAsync(session.Settings, CancellationToken.None);

        var missing = constraint.Evaluate(null);
        var malformed = constraint.Evaluate("{not json");

        missing.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.NotEvaluated);
        malformed.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.NotEvaluated);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"constraints\":{\"stack\":{\"type\":\"func-workload\",\"args\":\"node\"}}}")]
    public async Task EvaluateConstraintsAsync_PreCanceledToken_ThrowsForEveryConfiguration(string configuration)
    {
        using TemplateEngineSession session = new(CommandContext("canceled-evaluation"), Hive);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => session.EvaluateConstraintsAsync(configuration, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NativePreview_ConditionalPrimaryOutputs_PreservesRawActionIndexAndResolvedPath()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        File.WriteAllText(Path.Combine(package, "project", ".template.config", "template.json"),
            $$"""
            {
              "identity": "{{TemplateFixtures.ProjectTemplateIdentity}}",
              "name": "Project topology qualification",
              "shortName": "func-tests-project",
              "tags": { "type": "project" },
              "sourceName": "FuncProject",
              "sources": [ { "source": "./", "target": "FuncProject/" } ],
              "primaryOutputs": [
                { "path": "disabled/host.json", "condition": "false" },
                { "path": "FuncProject/host.json" }
              ],
              "postActions": [ {
                "actionId": "00e33184-ffc7-43ef-bec0-b1684df8ad56",
                "continueOnError": false,
                "args": { "primaryOutputIndex": "1", "stack": "python", "language": "python" }
              } ]
            }
            """);
        await TemplateFixtures.InstallAsync(package, Hive);
        using TemplateEngineSession session = new(CommandContext("project-preview"), Hive);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity);
        TemplateCreator creator = new(session.Settings);
        var output = DirectoryUnderRoot("project-output");

        var preview = await creator.InstantiateAsync(template, "Renamed", "Renamed", output.Info.FullName,
            new Dictionary<string, string?>(), dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        var effects = preview.CreationEffects!;
        effects.CreationResult.PrimaryOutputs.Should().ContainSingle();
        Path.GetFullPath(Path.Combine(output.Info.FullName, effects.CreationResult.PrimaryOutputs.Single().Path))
            .Should().Be(Path.Combine(output.Info.FullName, "Renamed", "host.json"));
        effects.CreationResult.PostActions.Should().ContainSingle().Which.Args["primaryOutputIndex"].Should().Be("1");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await creator.InstantiateAsync(template, "Renamed", "Renamed", output.Info.FullName,
            new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success);
        File.Exists(Path.Combine(output.Info.FullName, "Renamed", "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "Renamed", ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_GeneratedHost_PersistsCliStackShapeAtomically()
    {
        var project = DirectoryUnderRoot("configuration-project");
        Directory.CreateDirectory(project.Info.FullName);
        var host = Path.Combine(project.Info.FullName, "host.json");
        File.WriteAllText(host, "{\"version\":\"2.0\"}");
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());
        TemplateProjectConfigurationPlan plan = new(host, "python", "python");

        await writer.WriteAsync(plan, CancellationToken.None);

        using var config = JsonDocument.Parse(File.ReadAllText(plan.ConfigurationPath));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("python");
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("python");
        Directory.GetFiles(Path.GetDirectoryName(plan.ConfigurationPath)!).Should().ContainSingle().Which.Should().Be(plan.ConfigurationPath);
        File.ReadAllText(host).Should().Be("{\"version\":\"2.0\"}");
    }

    [Fact]
    public void ProjectConfigurationWriter_NullFileSystem_Throws()
    {
        Action construct = () => new TemplateProjectConfigurationWriter(null!);

        construct.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_DestinationAppearsAtCommit_PreservesUserBytes()
    {
        Directory.CreateDirectory(_root);
        var host = Path.Combine(_root, "host.json");
        File.WriteAllText(host, "{\"version\":\"2.0\"}");
        CommitBoundaryFileSystem fileSystem = new(path => File.WriteAllBytes(path, [0, 1, 255, 42]));
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new(host, "python", "Python");

        Func<Task> write = () => writer.WriteAsync(plan, CancellationToken.None);

        await write.Should().ThrowAsync<IOException>();
        File.ReadAllBytes(plan.ConfigurationPath).Should().Equal(0, 1, 255, 42);
        File.ReadAllText(host).Should().Be("{\"version\":\"2.0\"}");
        Directory.GetFiles(Path.GetDirectoryName(plan.ConfigurationPath)!).Should().Equal(plan.ConfigurationPath);
    }

    [Fact]
    public void ConfigurationExists_NullPlan_ThrowsWithoutFileSystemCalls()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);

        Action exists = () => writer.ConfigurationExists(null!);

        exists.Should().Throw<ArgumentNullException>().WithParameterName("plan");
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ConfigurationExists_FileOrDirectory_UsesInjectedAuthority(bool file, bool directory, bool occupied)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new(Path.Combine(_root, "host.json"), "python", "Python");
        fileSystem.FileExists(plan.ConfigurationPath).Returns(file);
        fileSystem.DirectoryExists(plan.ConfigurationPath).Returns(directory);

        var result = writer.ConfigurationExists(plan);

        result.Should().Be(occupied);
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_MissingPrimaryOutput_ReportsMissingFileWithoutCreatingConfiguration()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new(Path.Combine(_root, "host.json"), "python", "Python");

        Func<Task> write = () => writer.WriteAsync(plan, CancellationToken.None);

        var exception = (await write.Should().ThrowAsync<FileNotFoundException>()).Which;
        exception.FileName.Should().Be(plan.PrimaryOutput);
        exception.Message.Should().Be("The generated primary output is missing. Project configuration was not written.");
        await fileSystem.DidNotReceive().CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_ConfigurationDestinationIsDirectory_RefusesWithoutWriting()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new(Path.Combine(_root, "host.json"), "python", "Python");
        fileSystem.FileExists(plan.PrimaryOutput).Returns(true);
        fileSystem.DirectoryExists(plan.ConfigurationPath).Returns(true);

        Func<Task> write = () => writer.WriteAsync(plan, CancellationToken.None);

        await write.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.");
        await fileSystem.DidNotReceive().CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ProjectConfigurationPlanner_NullStackCatalog_Throws()
    {
        Action construct = () => new TemplateProjectConfigurationPlanner(null!);

        construct.Should().Throw<ArgumentNullException>().WithParameterName("stackCatalog");
    }

    [Fact]
    public void ReadProjectConfiguration_MutableStackMetadata_DoesNotChangeCapturedDeclarations()
    {
        List<string> languages = ["Python"];
        List<string> pythonAliases = ["py"];
        Dictionary<string, IReadOnlyList<string>> aliases = new(StringComparer.Ordinal) { ["Python"] = pythonAliases };
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(languages);
        stack.SupportedLanguageAliases.Returns(aliases);
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var configuration = File.ReadAllText(Path.Combine(package, "project", ".template.config", "template.json"));
        stack.Stack.Returns("changed-stack");
        languages[0] = "ChangedLanguage";
        pythonAliases[0] = "changed-alias";
        aliases["Python"] = ["replacement-alias"];
        aliases["ChangedLanguage"] = ["new-alias"];

        var declaration = planner.Read(configuration).Should().ContainSingle().Subject;

        declaration.Stack.Should().Be("python");
        declaration.Language.Should().Be("Python");
        Action readChangedAlias = () => planner.Read(configuration.Replace("\"py\"", "\"changed-alias\"", StringComparison.Ordinal));
        readChangedAlias.Should().Throw<InvalidTemplateMetadataException>()
            .WithMessage("Configuration language must identify exactly one supported stack language.");
    }

    [Fact]
    public void CreateProjectPlans_NullDirectoryOutputs_Throws()
    {
        TemplateProjectConfigurationPlanner planner = new(new([]));
        var effects = Substitute.For<ICreationEffects>();

        Action create = () => planner.CreatePlans("{}", effects, _root, null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("directoryOutputs");
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_NullPlan_ThrowsWithoutFileSystemCalls()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);

        Func<Task> write = () => writer.WriteAsync(null!, CancellationToken.None);

        await write.Should().ThrowAsync<ArgumentNullException>().WithParameterName("plan");
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(" ", typeof(ArgumentException))]
    public Task WriteProjectConfigurationAsync_InvalidPrimaryOutput_ThrowsWithoutFileSystemCalls(string? invalid, Type exceptionType)
        => AssertInvalidPlanAsync(new(invalid!, "python", "Python"), "PrimaryOutput", exceptionType);

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(" ", typeof(ArgumentException))]
    public Task WriteProjectConfigurationAsync_InvalidStack_ThrowsWithoutFileSystemCalls(string? invalid, Type exceptionType)
        => AssertInvalidPlanAsync(new(Path.Combine(_root, "host.json"), invalid!, "Python"), "Stack", exceptionType);

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(" ", typeof(ArgumentException))]
    public Task WriteProjectConfigurationAsync_InvalidLanguage_ThrowsWithoutFileSystemCalls(string? invalid, Type exceptionType)
        => AssertInvalidPlanAsync(new(Path.Combine(_root, "host.json"), "python", invalid!), "Language", exceptionType);

    private static async Task AssertInvalidPlanAsync(TemplateProjectConfigurationPlan plan, string field, Type exceptionType)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);

        Func<Task> write = () => writer.WriteAsync(plan, CancellationToken.None);

        var exception = (await write.Should().ThrowAsync<ArgumentException>().WithParameterName($"plan.{field}")).Which;
        exception.GetType().Should().Be(exceptionType);
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_RelativePrimaryOutput_ThrowsWithoutFileSystemCalls()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new("host.json", "python", "Python");

        Func<Task> write = () => writer.WriteAsync(plan, CancellationToken.None);

        await write.Should().ThrowAsync<ArgumentException>().WithParameterName("plan");
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task WriteProjectConfigurationAsync_PreCanceled_ThrowsWithoutFileSystemCalls()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        TemplateProjectConfigurationWriter writer = new(fileSystem);
        TemplateProjectConfigurationPlan plan = new(Path.Combine(_root, "host.json"), "python", "Python");
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> write = () => writer.WriteAsync(plan, cancellation.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("py")]
    [InlineData("PY")]
    [InlineData("python-alt")]
    [InlineData("Python")]
    public void ReadProjectConfiguration_CatalogAliasKeyCasing_PreservesCanonicalLanguage(string language)
    {
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("python");
        stack.DisplayName.Returns("Python");
        stack.SupportedLanguages.Returns(["Python"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Python"] = ["python-alt"],
            ["PYTHON"] = ["py"],
        });
        InstalledProjectStackCatalog catalog = new([stack]);
        TemplateProjectConfigurationPlanner planner = new(catalog);
        var configuration = $$$"""
            {
              "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
              "primaryOutputs":[{"path":"host.json"}],
              "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56",
                "args":{"primaryOutputIndex":"0","stack":"python","language":"{{{language}}}"}}]
            }
            """;
        catalog.GetCanonicalLanguage(language).Should().Be("Python");

        var declaration = planner.Read(configuration).Should().ContainSingle().Subject;

        declaration.Stack.Should().Be("python");
        declaration.Language.Should().Be("Python");
    }

    [Fact]
    public void ReadProjectConfiguration_ValidRawArguments_PreserveCanonicalMetadata()
    {
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([initializer]));
        var configuration = """
            {
              "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
              "primaryOutputs":[{"path":"host.json"}],
              "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56","continueOnError":false,
                "args":{"primaryOutputIndex":"0","stack":"PYTHON","language":"py"}}]
            }
            """;

        var declaration = planner.Read(configuration).Should().ContainSingle().Which;

        declaration.PrimaryOutputIndex.Should().Be(0);
        declaration.Stack.Should().Be("python");
        declaration.Language.Should().Be("Python");
    }

    [Theory]
    [InlineData("0", "false", "'primaryOutputIndex' must be a nonempty raw string.")]
    [InlineData("\"-1\"", "false", "primaryOutputIndex must reference an authored primary output using a nonnegative invariant integer string.")]
    [InlineData("\"1\"", "false", "primaryOutputIndex must reference an authored primary output using a nonnegative invariant integer string.")]
    [InlineData("\"0\"", "true", "Mandatory project configuration cannot continue on error.")]
    [InlineData("\"0\"", "\"false\"", "Mandatory project configuration cannot continue on error.")]
    public void ReadProjectConfiguration_InvalidRawArguments_ReportSpecificRule(string index, string continueOnError, string diagnostic)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var path = Path.Combine(package, "project", ".template.config", "template.json");
        var configuration = File.ReadAllText(path)
            .Replace("\"primaryOutputIndex\":\"0\"", $"\"primaryOutputIndex\":{index}", StringComparison.Ordinal)
            .Replace("\"actionId\":", $"\"continueOnError\":{continueOnError},\"actionId\":", StringComparison.Ordinal);
        TemplateProjectConfigurationPlanner planner = new(new([]));

        Action read = () => planner.Read(configuration);

        read.Should().Throw<InvalidTemplateMetadataException>().WithMessage(diagnostic);
    }

    [Theory]
    [InlineData("\"primaryOutputIndex\":\"0\",\"primaryOutputIndex\":\"0\",\"stack\":\"python\",\"language\":\"py\"",
        "Duplicate project configuration property 'primaryOutputIndex'.")]
    [InlineData("\"primaryOutputIndex\":\"0\",\"stack\":\"python\"",
        "Configuration arguments must be exactly primaryOutputIndex, stack and language.")]
    [InlineData("\"primaryOutputIndex\":\"0\",\"stack\":\"python\",\"language\":\"py\",\"extra\":\"value\"",
        "Configuration arguments must be exactly primaryOutputIndex, stack and language.")]
    [InlineData("\"primaryOutputIndex\":\"0\",\"stack\":false,\"language\":\"py\"", "'stack' must be a nonempty raw string.")]
    [InlineData("\"primaryOutputIndex\":\"0\",\"stack\":\"unknown\",\"language\":\"py\"",
        "Configuration stack must identify exactly one installed stack.")]
    [InlineData("\"primaryOutputIndex\":\"0\",\"stack\":\"python\",\"language\":\"unknown\"",
        "Configuration language must identify exactly one supported stack language.")]
    public void ReadProjectConfiguration_InvalidObjectShape_FailsClosed(string arguments, string diagnostic)
    {
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([initializer]));
        var configuration = $$$"""
            {
              "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
              "primaryOutputs":[{"path":"host.json"}],
              "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56","args":{ {{{arguments}}} }}]
            }
            """;

        Action read = () => planner.Read(configuration);

        read.Should().Throw<InvalidTemplateMetadataException>().WithMessage(diagnostic);
    }

    [Fact]
    public void ReadProjectConfiguration_MalformedUnicodeProperty_IsMetadataFailure()
    {
        TemplateProjectConfigurationPlanner planner = new(new([]));

        Action read = () => planner.Read("{\"extra\":{\"\\uD800\":1}}");

        read.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("{", typeof(JsonException))]
    [InlineData("{\"extra\":{\"\\uD800\":1}}", typeof(InvalidOperationException))]
    [InlineData("{\"constraints\":{\"host\":{\"type\":\"host\",\"args\":[{\"hostname\":\"func\"}]}},\"primaryOutputs\":[{\"path\":\"host.json\"}],\"sourceName\":\"\\uD800\"}", typeof(InvalidOperationException))]
    public void ReadProjectConfiguration_ParserFailure_PreservesInnerException(string configuration, Type exceptionType)
    {
        TemplateProjectConfigurationPlanner planner = new(new([]));

        Action read = () => planner.Read(configuration);

        var exception = read.Should().Throw<InvalidTemplateMetadataException>().Which;
        exception.InnerException.Should().BeAssignableTo(exceptionType);
        exception.InnerException!.StackTrace.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("sources", "[]", "Project topology property 'sources' is not qualified for this creation path.")]
    [InlineData("Sources", "[]", "Project topology property 'sources' is not qualified for this creation path.")]
    [InlineData("Symbols", "{}", "Duplicate project configuration property 'Symbols'.")]
    [InlineData("additionalConfigFiles", "[]", "Project topology property 'additionalConfigFiles' is not qualified for this creation path.")]
    [InlineData("baselines", "{}", "Project topology property 'baselines' is not qualified for this creation path.")]
    [InlineData("primaryOutputs", "[{\"path\":\"host.json\",\"condition\":\"true\"}]",
        "Only unconditional literal primary outputs are qualified for project configuration.")]
    [InlineData("postActions", "[{\"actionId\":\"17a32346-c721-42cd-a520-4d1cada22ba2\",\"args\":{}}]",
        "This project path supports only mandatory CLI configuration actions.")]
    public void ReadProjectConfiguration_UnqualifiedTopologyOrAction_FailsClosed(string property, string value, string diagnostic)
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var configuration = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(package, "project", ".template.config", "template.json")))!;
        configuration[property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var initializer = Substitute.For<IProjectStack>();
        initializer.Stack.Returns("python");
        initializer.DisplayName.Returns("Python");
        initializer.SupportedLanguages.Returns(["Python"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["py"] });
        TemplateProjectConfigurationPlanner planner = new(new([initializer]));

        Action read = () => planner.Read(configuration.ToJsonString());

        read.Should().Throw<InvalidTemplateMetadataException>().WithMessage(diagnostic);
    }

    [Fact]
    public void ReadProjectConfiguration_NonCanonicalSymbols_ReportsSpellingRule()
    {
        var package = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var configuration = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(package, "project", ".template.config", "template.json")))!.AsObject();
        configuration.Remove("symbols");
        configuration["Symbols"] = new System.Text.Json.Nodes.JsonObject();
        TemplateProjectConfigurationPlanner planner = new(new([]));

        Action read = () => planner.Read(configuration.ToJsonString());

        read.Should().Throw<InvalidTemplateMetadataException>()
            .WithMessage("Project symbol metadata must use the canonical symbols property name.");
    }

    [Fact]
    public void Settings_UseTheSuppliedSettingsLocation()
    {
        using TemplateEngineSession session = new(CommandContext("command"), Hive);

        session.Settings.Paths.GlobalSettingsDir.Should().Be(Hive);
    }

    [Fact]
    public void Settings_SeparateSessionsOnSharedHive_KeepTheirOwnContext()
    {
        using TemplateEngineSession node = new(ProjectContext("node-app", "node"), Hive);
        using TemplateEngineSession python = new(ProjectContext("python-app", "python"), Hive);

        HostParameter(node, FuncTemplateEngineHostParameters.Stack).Should().Be("node");
        HostParameter(python, FuncTemplateEngineHostParameters.Stack).Should().Be("python");
        HostParameter(node, "WorkingDirectory").Should().Be(DirectoryUnderRoot("node-app").Info.FullName);
        HostParameter(python, "WorkingDirectory").Should().Be(DirectoryUnderRoot("python-app").Info.FullName);
        node.Settings.Should().NotBeSameAs(python.Settings);
    }

    [Fact]
    public void Dispose_ThenUse_ThrowsObjectDisposedException()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive);

        session.Dispose();

        Action readSettings = () => _ = session.Settings;
        Action readPackageManager = () => _ = session.PackageManager;
        readSettings.Should().Throw<ObjectDisposedException>();
        readPackageManager.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive);
        session.Dispose();

        Action act = session.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new TemplateEngineSession(null!, Hive);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData("Planner")]
    [InlineData("Writer")]
    public void ProjectSupport_MissingDependency_IdentifiesMissingArgument(string missing)
    {
        TemplateProjectConfigurationPlanner planner = new(new([]));
        TemplateProjectConfigurationWriter writer = new(new PhysicalFileSystem());

        Action construct = () => new TemplateProjectSupport(missing == "Planner" ? null! : planner, missing == "Writer" ? null! : writer);

        construct.Should().Throw<ArgumentNullException>().WithParameterName(missing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSettingsLocation_Throws(string settingsLocation)
    {
        Action act = () => _ = new TemplateEngineSession(CommandContext("command"), settingsLocation);

        act.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
    }

    private static string? HostParameter(TemplateEngineSession session, string name)
    {
        session.Settings.Host.TryGetHostParamDefault(name, out string? value).Should().BeTrue();
        return value;
    }

    private WorkingDirectory DirectoryUnderRoot(string name) => WorkingDirectory.FromExplicit(Path.Combine(_root, name));

    private TemplateEngineContext CommandContext(string directory) => new(DirectoryUnderRoot(directory));

    private TemplateEngineContext ProjectContext(string directory, string stack)
        => new(DirectoryUnderRoot(directory), new TemplateEngineProjectContext(DirectoryUnderRoot(directory), stack));
}
