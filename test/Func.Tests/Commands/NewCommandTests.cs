// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Projects.FileWriting;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly ITemplaterFactory _templaterFactory;
    private readonly IFileSystem _fileSystem;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _projectResolver = Substitute.For<IFunctionsProjectResolver>();
        _projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProjectResolutionResult>(ProjectResolutionResults.NotResolved("No installed workload recognized this directory.")));
        _templaterFactory = Substitute.For<ITemplaterFactory>();
        _fileSystem = Substitute.For<IFileSystem>();
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        NewCommand cmd = CreateCommand();
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
    public async Task NewCommand_List_FailsWithMigrationDiagnostic(string format)
    {
        NewCommand command = CreateCommand();
        ParseResult result = command.Parse(["--list", "--format", format]);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("not available");
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    [InlineData("--output:json")]
    public void NewCommand_RemovedOutputOption_ReportsUnrecognizedOption(params string[] args)
    {
        NewCommand command = CreateCommand();
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
        NewCommand command = CreateCommand();
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
        NewCommand cmd = CreateCommand();
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
            NewCommand command = CreateCommand();
            string[] args = [directory, "--template", "HttpTrigger", "--name", "Example", "--non-interactive"];
            if (force)
            {
                args = [.. args, "--force"];
            }

            int exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            await _templaterFactory.DidNotReceive().CreateAsync(
                Arg.Any<TemplateEngineContext>(),
                Arg.Any<CancellationToken>());
            string expectedDiagnostic = force
                ? "Overwriting files is not supported for template generation; existing files are preserved."
                : "No installed workload recognized";
            _interaction.AllOutput.Should().Contain(expectedDiagnostic);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCommand_UnresolvedDirectory_StopsBeforeTemplateEffects(bool inspectionFails)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-new-uninitialized-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
            factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectCreationResults.NotCreated("not mine"));
            IFileSystem inspectionFileSystem = inspectionFails ? Substitute.For<IFileSystem>() : new PhysicalFileSystem();
            if (inspectionFails)
            {
                inspectionFileSystem.DirectoryHasEntries(directory).Returns(_ => throw new UnauthorizedAccessException());
            }
            IFunctionsProjectResolver resolver = new FunctionsProjectResolver(
            [
                new WorkloadProjectFactoryRegistration(TestWorkloads.CreateInfo("Test.Workload"), factory)
            ], inspectionFileSystem);
            IProjectStack stack = Substitute.For<IProjectStack>();
            stack.Stack.Returns("test");
            stack.DisplayName.Returns("Test");
            stack.WorkerRuntimeAliases.Returns([]);
            stack.SupportedLanguages.Returns(["Test"]);
            stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
            WindowsProjectFileSystem projectFileSystem = Substitute.For<WindowsProjectFileSystem>();
            NewCommand command = new(
                _interaction,
                resolver,
                new InstalledProjectStackCatalog([stack]),
                _templaterFactory,
                new ProjectTemplateInputLease(_fileSystem),
                new ProjectFileWriter(projectFileSystem));

            int exitCode = await command.Parse(
                [directory, "--template", "HttpTrigger", "--name", "Example", "--non-interactive"])
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            string expected = inspectionFails
                ? "The project directory could not be inspected. Check that it exists and is accessible, then try again."
                : "This directory is not an initialized Azure Functions project. Run 'func init' to initialize a Functions project.";
            _interaction.AllOutput.Should().Contain(expected);
            if (inspectionFails)
            {
                _interaction.AllOutput.Should().NotContain("func init");
            }
            await factory.Received(1).TryCreateProjectAsync(
                Arg.Any<ProjectCreationContext>(),
                Arg.Any<CancellationToken>());
            await stack.DidNotReceive().EvaluateTemplateCompatibilityAsync(
                Arg.Any<FunctionsProject>(),
                Arg.Any<Func<IReadOnlyList<string>, CancellationToken, IDisposable>>(),
                Arg.Any<CancellationToken>());
            await _templaterFactory.DidNotReceive().CreateAsync(
                Arg.Any<TemplateEngineContext>(),
                Arg.Any<CancellationToken>());
            projectFileSystem.DidNotReceive().OpenSession(Arg.Any<string>(), Arg.Any<CancellationToken>());
            Directory.GetFileSystemEntries(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewCommand_NullInteraction_Throws()
    {
        Action act = () => _ = new NewCommand(
            null!,
            _projectResolver,
            new InstalledProjectStackCatalog(Array.Empty<IProjectStack>()),
            _templaterFactory,
            new ProjectTemplateInputLease(_fileSystem),
            new ProjectFileWriter(new WindowsProjectFileSystem()));

        act.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    private NewCommand CreateCommand() => new(
        _interaction,
        _projectResolver,
        new InstalledProjectStackCatalog(Array.Empty<IProjectStack>()),
        _templaterFactory,
        new ProjectTemplateInputLease(_fileSystem),
        new ProjectFileWriter(new WindowsProjectFileSystem()));
}
