// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public sealed class QuickstartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartManifestClient _manifestClient = Substitute.For<IQuickstartManifestClient>();
    private readonly IQuickstartScaffolder _scaffolder = Substitute.For<IQuickstartScaffolder>();

    public QuickstartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-quickstart-cmd-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- Structural -----------------------------------------------

    [Fact]
    public void QuickstartCommand_HasExpectedOptionsAndSubcommands()
    {
        QuickstartCommand cmd = CreateCommand();

        Assert.Contains(cmd.Options, o => o.Name == "--language");
        Assert.Contains(cmd.Options, o => o.Name == "--template");
        Assert.Contains(cmd.Options, o => o.Name == "--resource");
        Assert.Contains(cmd.Options, o => o.Name == "--iac");
        Assert.Contains(cmd.Options, o => o.Name == "--search");
        Assert.Contains(cmd.Options, o => o.Name == "--fetch");

        Assert.Contains(cmd.Subcommands, c => c.Name == "list");
        Assert.Contains(cmd.Subcommands, c => c.Name == "info");
        Assert.Single(cmd.Arguments, a => a.Name == "path");
    }

    // --- --template direct scaffold ------------------------------

    [Fact]
    public async Task Quickstart_TemplateFlag_ScaffoldsByIdWithoutPrompt()
    {
        StubManifest(Entry("http-py", language: "Python"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--template", "http-py", _tempDir);

        Assert.Equal(0, exit);
        await _scaffolder.Received(1).ScaffoldAsync(
            Arg.Is<QuickstartEntry>(e => e.Id == "http-py"),
            _tempDir,
            FetchMode.Auto,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quickstart_TemplateFlag_UnknownId_ThrowsGracefulException()
    {
        StubManifest(Entry("http-py"));
        QuickstartCommand cmd = CreateCommand();

        var ex = await Assert.ThrowsAsync<GracefulException>(() =>
            InvokeAsync(cmd, "--template", "does-not-exist", _tempDir));

        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("func quickstart list", ex.Message);
    }

    [Fact]
    public async Task Quickstart_FetchFlag_ForwardsToScaffolder()
    {
        StubManifest(Entry("http-py"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--template", "http-py", "--fetch", "git", _tempDir);

        Assert.Equal(0, exit);
        await _scaffolder.Received(1).ScaffoldAsync(
            Arg.Any<QuickstartEntry>(),
            Arg.Any<string>(),
            FetchMode.Git,
            Arg.Any<CancellationToken>());
    }

    // --- Interactive (no --template) -----------------------------

    [Fact]
    public async Task Quickstart_NoFilters_PromptsAndScaffolds()
    {
        StubManifest(
            Entry("a", "Python", "HTTP Trigger"),
            Entry("b", "CSharp", "Timer Trigger"));
        QuickstartCommand cmd = CreateCommand();

        // TestInteractionService.PromptForSelectionAsync returns first choice.
        int exit = await InvokeAsync(cmd, _tempDir);

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("SELECT:"));
        await _scaffolder.Received(1).ScaffoldAsync(
            Arg.Is<QuickstartEntry>(e => e.Id == "a"),
            _tempDir,
            FetchMode.Auto,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quickstart_NoMatchingFilters_ExitsOneWithHint()
    {
        StubManifest(Entry("py", "Python"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--language", "java", _tempDir);

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("No templates matched"));
        await _scaffolder.DidNotReceive().ScaffoldAsync(
            Arg.Any<QuickstartEntry>(), Arg.Any<string>(),
            Arg.Any<FetchMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quickstart_SearchFilterNoMatches_HintMentionsSearchTerm()
    {
        StubManifest(Entry("py", "Python"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--search", "kafka", _tempDir);

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines,
            l => l.StartsWith("HINT:") && l.Contains("kafka"));
    }

    // --- Language resolution -------------------------------------

    [Fact]
    public async Task Quickstart_DotnetLanguage_MapsToCSharpWithoutPrompt()
    {
        // --language dotnet silently maps to CSharp (same as `func init`).
        StubManifest(
            Entry("cs", "CSharp"),
            Entry("fs", "FSharp"),
            Entry("py", "Python"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--language", "dotnet", _tempDir);

        Assert.Equal(0, exit);
        // No language sub-prompt is emitted; only the template selection.
        var selectLines = _interaction.Lines.Where(l => l.StartsWith("SELECT:")).ToList();
        Assert.All(selectLines, l => Assert.DoesNotContain("FSharp", l));
        // Only the CSharp candidate is offered.
        Assert.Contains(selectLines, l => l.Contains("cs"));
    }

    [Fact]
    public async Task Quickstart_NodeLanguage_MapsToTypeScriptWithoutPrompt()
    {
        // --language node silently maps to TypeScript (same as `func init`).
        StubManifest(
            Entry("ts", "TypeScript"),
            Entry("js", "JavaScript"));
        QuickstartCommand cmd = CreateCommand();

        int exit = await InvokeAsync(cmd, "--language", "node", _tempDir);

        Assert.Equal(0, exit);
        var selectLines = _interaction.Lines.Where(l => l.StartsWith("SELECT:")).ToList();
        Assert.All(selectLines, l => Assert.DoesNotContain("JavaScript", l));
        Assert.Contains(selectLines, l => l.Contains("ts"));
    }

    [Fact]
    public async Task Quickstart_UnknownLanguage_ThrowsGracefulException()
    {
        StubManifest(Entry("py", "Python"));
        QuickstartCommand cmd = CreateCommand();

        var ex = await Assert.ThrowsAsync<GracefulException>(() =>
            InvokeAsync(cmd, "--language", "ruby", _tempDir));

        Assert.Contains("ruby", ex.Message);
    }

    // --- Manifest fetch failure ----------------------------------

    [Fact]
    public async Task Quickstart_ManifestFetchFails_ThrowsGracefulException()
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns<QuickstartManifest>(_ => throw new InvalidOperationException("offline"));
        QuickstartCommand cmd = CreateCommand();

        var ex = await Assert.ThrowsAsync<GracefulException>(() =>
            InvokeAsync(cmd, _tempDir));

        Assert.Contains("offline", ex.Message);
    }

    [Fact]
    public async Task Quickstart_ScaffoldFails_ThrowsGracefulException()
    {
        StubManifest(Entry("py", "Python"));
        _scaffolder.ScaffoldAsync(
            Arg.Any<QuickstartEntry>(), Arg.Any<string>(),
            Arg.Any<FetchMode>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("disk full"));
        QuickstartCommand cmd = CreateCommand();

        var ex = await Assert.ThrowsAsync<GracefulException>(() =>
            InvokeAsync(cmd, "--template", "py", _tempDir));

        Assert.Contains("disk full", ex.Message);
    }

    // --- Helpers -------------------------------------------------

    private QuickstartCommand CreateCommand()
    {
        var listCommand = new QuickstartListCommand(_interaction, _manifestClient);
        var infoCommand = new QuickstartInfoCommand(_interaction, _manifestClient);
        return new QuickstartCommand(
            listCommand, infoCommand, _interaction, _manifestClient, _scaffolder);
    }

    private void StubManifest(params QuickstartEntry[] entries)
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest(entries));
    }

    private static QuickstartEntry Entry(
        string id, string language = "Python", string resource = "HTTP Trigger") =>
        new()
        {
            Id = id,
            DisplayName = id,
            Language = language,
            Resource = resource,
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
        };

    private static Task<int> InvokeAsync(FuncCliCommand command, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
        ParseResult result = root.Parse(new[] { command.Name }.Concat(args).ToArray());
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return result.InvokeAsync(config);
    }
}
