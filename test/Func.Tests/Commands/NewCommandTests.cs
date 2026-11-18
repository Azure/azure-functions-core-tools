// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"func-new-tests-{Guid.NewGuid():N}");
    private readonly string _project;
    private readonly string _templatesHome;
    private readonly TestInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly TemplaterFactory _templaterFactory;

    public NewCommandTests()
    {
        _project = Path.Combine(_root, "project");
        _templatesHome = Path.Combine(_root, "home");
        _interaction = new TestInteractionService();
        _projectResolver = Substitute.For<IFunctionsProjectResolver>();
        _projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.NotResolved("no project"));
        _templaterFactory = new TemplaterFactory(new CliConfigurationPathsOptions(_templatesHome), []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

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
    public async Task NewCommand_List_FailsWithMigrationDiagnostic(string format)
    {
        var command = CreateCommand();
        ParseResult result = command.Parse(["--list", "--format", format]);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("temporarily unavailable");
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCommand_NotInitialized_FailsWithInitGuidanceWithoutChanges(bool directoryExists)
    {
        if (directoryExists)
        {
            Directory.CreateDirectory(_project);
            await File.WriteAllTextAsync(Path.Combine(_project, "host.json"), "{}");
        }

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(_project, "--template", "http", "--name", "Example"))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("Run 'func init' first");
        await _projectResolver.DidNotReceiveWithAnyArgs().ResolveProjectAsync(default!, default);
        Directory.Exists(_project).Should().Be(directoryExists);
        if (directoryExists)
        {
            Directory.GetFileSystemEntries(_project).Should().Equal(Path.Combine(_project, "host.json"));
        }
    }

    [Fact]
    public async Task NewCommand_UnresolvedProject_FailsWithResolverMessage()
    {
        WriteProjectConfig();

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(_project, "--template", "http", "--name", "Example"))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Be("no project");
    }

    [Theory]
    [InlineData("--template", "http")]
    [InlineData("--name", "Example")]
    public async Task NewCommand_NonInteractiveMissingTemplateOrName_FailsWithoutChanges(params string[] args)
    {
        await InitializeProjectAsync();

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(_project, args))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("--template").And.Contain("--name");
        File.Exists(Path.Combine(_project, "Example.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task NewCommand_ProjectWithoutLanguage_FailsWithoutChanges()
    {
        await InitializeProjectAsync(language: null);

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(_project, "--template", "http", "--name", "Example"))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("No function templates are available");
        File.Exists(Path.Combine(_project, "Example.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task NewCommand_UnknownTemplate_FailsWithoutChanges()
    {
        await InitializeProjectAsync();

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(_project, "--template", "timer", "--name", "Example"))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("'timer'").And.Contain("C#");
        File.Exists(Path.Combine(_project, "Example.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("func-tests-item")]
    [InlineData("Func.Tests.Http")]
    public async Task NewCommand_Template_CreatesFunctionInResolvedProject(string reference)
    {
        await InitializeProjectAsync();

        int exitCode = await InvokeAsync(_project, "--template", reference, "--name", "Example", "--non-interactive");

        exitCode.Should().Be(0);
        (await File.ReadAllTextAsync(Path.Combine(_project, "Example.txt"))).Should().Be("Example content");
        _interaction.AllOutput.Should().Contain("Created function").And.Contain("Example");
    }

    [Fact]
    public async Task NewCommand_ExactIdentity_TakesPrecedenceOverAnotherTemplatesName()
    {
        await InitializeProjectAsync();
        await InstallCollidingItemTemplateAsync();

        int exitCode = await InvokeAsync(_project, "--template", "Func.Tests.Http", "--name", "Example", "--non-interactive");

        exitCode.Should().Be(0);
        _interaction.AllOutput.Should().Contain("from template Http.");
        File.Exists(Path.Combine(_project, "Example.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task NewCommand_AmbiguousAlias_FailsWithoutChanges()
    {
        await InitializeProjectAsync();
        await InstallCollidingItemTemplateAsync();

        GracefulException ex = (await FluentActions.Awaiting(
            () => InvokeAsync(_project, "--template", "func-tests-item", "--name", "Example", "--non-interactive"))
            .Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("Multiple function templates").And.Contain("Func.Tests.Http").And.Contain("Func.Tests.Other");
        File.Exists(Path.Combine(_project, "Example.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task NewCommand_InteractiveWithoutTemplateOrName_PromptsForBoth()
    {
        await InitializeProjectAsync();
        InteractiveTestInteractionService interaction = new();

        int exitCode = await InvokeAsync(interaction, _project);

        exitCode.Should().Be(0);
        interaction.AllOutput.Should().Contain("SELECT: Select a template: [Http]").And.Contain("INPUT: Function name: (default: Function1)");
        File.Exists(Path.Combine(_project, "Function1.txt")).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, "existing")]
    [InlineData(true, "Example content")]
    public async Task NewCommand_ExistingFile_OverwritesOnlyWhenForced(bool force, string expectedContent)
    {
        await InitializeProjectAsync();
        string file = Path.Combine(_project, "Example.txt");
        await File.WriteAllTextAsync(file, "existing");
        string[] args = ["--template", "http", "--name", "Example"];
        Func<Task<int>> act = () => InvokeAsync(_project, force ? [.. args, "--force"] : args);

        if (force)
        {
            (await act()).Should().Be(0);
        }
        else
        {
            GracefulException ex = (await act.Should().ThrowAsync<GracefulException>()).Which;
            ex.Message.Should().Contain("Example.txt").And.Contain("--force");
        }

        (await File.ReadAllTextAsync(file)).Should().Be(expectedContent);
    }

    [Fact]
    public void NewCommand_NullInteraction_Throws()
    {
        Action act = () => _ = new NewCommand(null!, _projectResolver, _templaterFactory);

        act.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    [Fact]
    public void NewCommand_NullProjectResolver_Throws()
    {
        Action act = () => _ = new NewCommand(_interaction, null!, _templaterFactory);

        act.Should().Throw<ArgumentNullException>().WithParameterName("projectResolver");
    }

    [Fact]
    public void NewCommand_NullTemplaterFactory_Throws()
    {
        Action act = () => _ = new NewCommand(_interaction, _projectResolver, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("templaterFactory");
    }

    private NewCommand CreateCommand(TestInteractionService? interaction = null)
        => new(interaction ?? _interaction, _projectResolver, _templaterFactory);

    private Task<int> InvokeAsync(string directory, params string[] args) => InvokeAsync(_interaction, directory, args);

    private Task<int> InvokeAsync(TestInteractionService interaction, string directory, params string[] args)
        => CreateCommand(interaction).Parse([directory, .. args]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

    private void WriteProjectConfig()
    {
        string configPath = CliConfigurationPathsOptions.GetProjectConfigPath(new DirectoryInfo(_project));
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, """{"stack":{"runtime":"dotnet","language":"C#"}}""");
    }

    private async Task InstallCollidingItemTemplateAsync()
    {
        string package = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "other-packages"), "Func.Tests.Other", "Func.Tests.Http", "C#");
        await TemplateFixtures.InstallAsync(package, Path.Combine(_templatesHome, "templates"));
    }

    private async Task InitializeProjectAsync(string? language = "C#")
    {
        WriteProjectConfig();
        FunctionsProject project = Substitute.For<FunctionsProject>();
        project.WorkingDirectory.Returns(WorkingDirectory.FromExplicit(_project));
        project.StackName.Returns("dotnet");
        project.StackDisplayName.Returns(".NET");
        project.Language.Returns(language);
        _projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "resolved"));

        string package = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "packages"), "Func.Tests.Http", "Http", "C#");
        await TemplateFixtures.InstallAsync(package, Path.Combine(_templatesHome, "templates"));
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
