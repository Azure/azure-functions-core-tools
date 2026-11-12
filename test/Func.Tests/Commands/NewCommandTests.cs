// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Tests.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver = Substitute.For<IFunctionsProjectResolver>();
    private readonly InstalledProjectStackCatalog _stackCatalog = new([]);
    private readonly ITemplaterFactory _templaterFactory = Substitute.For<ITemplaterFactory>();

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
    }

    private NewCommand CreateCommand() => new(_interaction, _projectResolver, _stackCatalog, _templaterFactory);

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = CreateCommand();
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--name");
        optionNames.Should().Contain("--template");
        optionNames.Should().Contain("--force");
        optionNames.Should().Contain("--non-interactive");
        optionNames.Should().Contain("--list");
        optionNames.Should().Contain("--format");
        optionNames.Should().NotContain("--output");
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task NewCommand_List_RequiresInitializedProject(string format)
    {
        var command = CreateCommand();
        ParseResult result = command.Parse(["--list", "--format", format]);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("No initialized Functions project was found");
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    [InlineData("--output:json")]
    public void NewCommand_RemovedOutputOption_ReportsUnrecognizedOption(params string[] args)
    {
        var command = CreateCommand();
        ParseResult result = command.Parse(["--list", .. args]);

        result.Errors.Should().Contain(error =>
            error.Message.Contains("Unrecognized", StringComparison.Ordinal)
            && error.Message.Contains("--output", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    [InlineData("--output:json")]
    public void NewCommand_RemovedOutputOptionWithExplicitPath_ReportsUnrecognizedOption(params string[] args)
    {
        var command = CreateCommand();
        ParseResult result = command.Parse([".", "--list", .. args]);

        result.Errors.Should().Contain(error =>
            error.Message.Contains("Unrecognized", StringComparison.Ordinal)
            && error.Message.Contains("--output", StringComparison.Ordinal));
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("new");
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = CreateCommand();
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    // Single-dash typos like `-name` must surface as unrecognized options, not "needs a project".
    [Fact]
    public void NewCommand_SingleDashLongOption_ReportsUnrecognizedOption()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(
            new[] { "new", "--template", "HttpTrigger-Python", "-name", "ttpt" },
            new ParserConfiguration { EnablePosixBundling = false });

        result.Errors.Should().Contain(e => e.Message.Contains("Unrecognized option '-name'", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NewCommand_Unavailable_LeavesTargetUnchanged(bool exists, bool force)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-new-unavailable-{Guid.NewGuid():N}");
        string configDirectory = Path.Combine(directory, ".func");
        string configPath = Path.Combine(configDirectory, "config.json");
        const string config = """{"stack":{"runtime":"node"},"custom":"keep"}""";
        if (exists)
        {
            Directory.CreateDirectory(configDirectory);
            await File.WriteAllTextAsync(configPath, config);
            await File.WriteAllTextAsync(Path.Combine(directory, "host.json"), "{}");
            Directory.CreateDirectory(Path.Combine(directory, "empty"));
        }

        try
        {
            var command = CreateCommand();
            string[] args = [directory, "--template", "HttpTrigger", "--name", "Example", "--non-interactive"];
            if (force)
            {
                args = [.. args, "--force"];
            }

            int exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("No initialized Functions project was found");
            Directory.Exists(directory).Should().Be(exists);
            if (exists)
            {
                Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Should().BeEquivalentTo([configDirectory, configPath, Path.Combine(directory, "host.json"), Path.Combine(directory, "empty")]);
                (await File.ReadAllTextAsync(configPath)).Should().Be(config);
                (await File.ReadAllTextAsync(Path.Combine(directory, "host.json"))).Should().Be("{}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewCommand_Force_RefusesOverwriteForInitializedDotNetProject()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-new-force-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, ".func"));
        await File.WriteAllTextAsync(CliConfigurationPathsOptions.GetProjectConfigPath(new DirectoryInfo(directory)), "{}");
        try
        {
            FunctionsProject project = Substitute.For<FunctionsProject>();
            project.WorkingDirectory.Returns(WorkingDirectory.FromExplicit(directory));
            project.StackName.Returns("dotnet");
            project.Language.Returns("C#");
            _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(project, "test project"));

            IProjectStack stack = Substitute.For<IProjectStack>();
            stack.Stack.Returns("dotnet");
            stack.DisplayName.Returns(".NET");
            stack.WorkerRuntimeAliases.Returns(["dotnet-isolated"]);
            stack.SupportedLanguages.Returns(["C#"]);
            stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
            {
                ["C#"] = ["csharp"],
            });
            InstalledProjectStackCatalog catalog = new([stack]);
            var command = new NewCommand(_interaction, _projectResolver, catalog, _templaterFactory);

            int exitCode = await command.Parse([directory, "--force"])
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("overwrite behavior for item templates has not been approved");
            _templaterFactory.DidNotReceiveWithAnyArgs().Create(default!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewCommand_CreatesInstalledHttpTemplateAndRunsDeclaredPostActions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-new-template-{Guid.NewGuid():N}");
        string projectDirectory = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        FunctionsProject project = CreateInitializedDotNetProject(projectDirectory);
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "test project"));
        IProjectStack stack = CreateDotNetStack();
        stack.RunTemplatePostActionAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<WorkingDirectory>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        string settingsPath = Path.Combine(home, "templates");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteHttpItemPackage(root), settingsPath);
        NewCommand command = new(_interaction, _projectResolver, new InstalledProjectStackCatalog([stack]), new TemplaterFactory(new CliConfigurationPathsOptions(home)));

        try
        {
            int exitCode = await command.Parse([
                projectDirectory,
                "--template", "http",
                "--name", "SampleHttp",
                "--non-interactive",
            ]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(0);
            _interaction.AllOutput.Should().Contain("Created function from template");
            Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Should().ContainSingle();
            await stack.Received(1).RunTemplatePostActionAsync(
                TemplateFixtures.AddPackageActionId.ToLowerInvariant(),
                Arg.Is<IReadOnlyDictionary<string, string>>(arguments =>
                    arguments["referenceType"] == "package"
                    && arguments["reference"] == "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore"
                    && arguments["version"] == "2.1.1"),
                project.WorkingDirectory,
                Arg.Any<CancellationToken>());
            await stack.Received(1).RunTemplatePostActionAsync(
                TemplateFixtures.OpenEditorActionId.ToLowerInvariant(),
                Arg.Is<IReadOnlyDictionary<string, string>>(arguments => arguments["files"] == "0"),
                project.WorkingDirectory,
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewCommand_OverwriteConflictFailsAndPreservesExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-new-overwrite-{Guid.NewGuid():N}");
        string projectDirectory = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        FunctionsProject project = CreateInitializedDotNetProject(projectDirectory);
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "test project"));
        string existingFile = Path.Combine(projectDirectory, "HttpTriggerCSharp.cs");
        const string existingContent = "preserve this function";
        await File.WriteAllTextAsync(existingFile, existingContent);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteHttpItemPackage(root), Path.Combine(home, "templates"));
        NewCommand command = new(_interaction, _projectResolver, new InstalledProjectStackCatalog([CreateDotNetStack()]), new TemplaterFactory(new CliConfigurationPathsOptions(home)));

        try
        {
            int exitCode = await command.Parse([
                projectDirectory,
                "--template", "http",
                "--non-interactive",
            ]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("would overwrite existing files");
            (await File.ReadAllTextAsync(existingFile)).Should().Be(existingContent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewCommand_PostActionFailureReportsPartialCreation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-new-post-action-{Guid.NewGuid():N}");
        string projectDirectory = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        FunctionsProject project = CreateInitializedDotNetProject(projectDirectory);
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "test project"));
        IProjectStack stack = CreateDotNetStack();
        stack.RunTemplatePostActionAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<WorkingDirectory>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("package add failed")));
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteHttpItemPackage(root), Path.Combine(home, "templates"));
        NewCommand command = new(_interaction, _projectResolver, new InstalledProjectStackCatalog([stack]), new TemplaterFactory(new CliConfigurationPathsOptions(home)));

        try
        {
            int exitCode = await command.Parse([
                projectDirectory,
                "--template", "http",
                "--non-interactive",
            ]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("Function files were created, but template post-action");
            _interaction.AllOutput.Should().Contain("package add failed");
            Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewCommand_List_ExcludesIncompleteHttpTemplateMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-new-list-{Guid.NewGuid():N}");
        string projectDirectory = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        FunctionsProject project = CreateInitializedDotNetProject(projectDirectory);
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "test project"));
        string settingsPath = Path.Combine(home, "templates");
        await TemplateFixtures.InstallAsync(
            TemplateFixtures.WriteHttpItemPackage(root, includeIncompleteCandidate: true),
            settingsPath);
        NewCommand command = new(_interaction, _projectResolver, new InstalledProjectStackCatalog([CreateDotNetStack()]), new TemplaterFactory(new CliConfigurationPathsOptions(home)));

        try
        {
            int exitCode = await command.Parse([projectDirectory, "--list", "--format", "json"])
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(0);
            string json = _interaction.AllOutput["JSON: ".Length..];
            using var document = JsonDocument.Parse(json);
            var templates = document.RootElement.EnumerateArray().ToArray();
            templates.Should().ContainSingle();
            templates[0].GetProperty("identity").GetString().Should().Be(TemplateFixtures.HttpItemTemplateIdentity);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewCommand_AmbiguousHttpTemplates_FailsWithoutCreatingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-new-ambiguous-{Guid.NewGuid():N}");
        string projectDirectory = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        FunctionsProject project = CreateInitializedDotNetProject(projectDirectory);
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "test project"));
        string settingsPath = Path.Combine(home, "templates");
        await TemplateFixtures.InstallAsync(
            TemplateFixtures.WriteHttpItemPackage(root, includeAmbiguousCandidate: true),
            settingsPath);
        NewCommand command = new(_interaction, _projectResolver, new InstalledProjectStackCatalog([CreateDotNetStack()]), new TemplaterFactory(new CliConfigurationPathsOptions(home)));

        try
        {
            int exitCode = await command.Parse([projectDirectory, "--template", "http", "--non-interactive"])
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain(TemplateFixtures.HttpItemTemplateIdentity);
            _interaction.AllOutput.Should().Contain(TemplateFixtures.SecondHttpItemTemplateIdentity);
            File.Exists(Path.Combine(projectDirectory, "HttpTriggerCSharp.cs")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static FunctionsProject CreateInitializedDotNetProject(string projectDirectory)
    {
        Directory.CreateDirectory(Path.Combine(projectDirectory, ".func"));
        File.WriteAllText(CliConfigurationPathsOptions.GetProjectConfigPath(new DirectoryInfo(projectDirectory)), """{"stack":{"runtime":"dotnet","language":"C#"}}""");
        File.WriteAllText(Path.Combine(projectDirectory, "app.csproj"), "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        FunctionsProject project = Substitute.For<FunctionsProject>();
        project.WorkingDirectory.Returns(WorkingDirectory.FromExplicit(projectDirectory));
        project.StackName.Returns("dotnet");
        project.Language.Returns("C#");
        return project;
    }

    private static IProjectStack CreateDotNetStack()
    {
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("dotnet");
        stack.DisplayName.Returns(".NET");
        stack.WorkerRuntimeAliases.Returns(["dotnet-isolated"]);
        stack.SupportedLanguages.Returns(["C#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["C#"] = ["csharp"],
        });
        return stack;
    }

    [Fact]
    public void NewCommand_NullInteraction_Throws()
    {
        Action act = () => _ = new NewCommand(null!, Substitute.For<IFunctionsProjectResolver>(),
            _stackCatalog, _templaterFactory);

        act.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }
}
