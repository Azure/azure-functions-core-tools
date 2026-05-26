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

public sealed class QuickstartListCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartManifestClient _manifestClient = Substitute.For<IQuickstartManifestClient>();

    [Fact]
    public void QuickstartListCommand_HasExpectedOptions()
    {
        var cmd = new QuickstartListCommand(_interaction, _manifestClient);

        Assert.Contains(cmd.Options, o => o.Name == "--language");
        Assert.Contains(cmd.Options, o => o.Name == "--resource");
        Assert.Contains(cmd.Options, o => o.Name == "--iac");
        Assert.Contains(cmd.Options, o => o.Name == "--search");
    }

    [Fact]
    public async Task List_NoFilters_WritesTableOfAllEntries()
    {
        StubManifest(
            Entry("py-http", "Python", "HTTP Trigger"),
            Entry("cs-timer", "CSharp", "Timer Trigger"));

        int exit = await InvokeAsync("list");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("TABLE:"));
        Assert.Contains(_interaction.Lines, l => l.Contains("py-http"));
        Assert.Contains(_interaction.Lines, l => l.Contains("cs-timer"));
    }

    [Fact]
    public async Task List_OmitsLanguageColumn_WhenLanguageFilterApplied()
    {
        StubManifest(Entry("py-http", "Python", "HTTP Trigger"));

        int exit = await InvokeAsync("list", "--language", "Python");

        Assert.Equal(0, exit);
        string header = _interaction.Lines.First(l => l.StartsWith("TABLE:"));
        Assert.DoesNotContain("Language", header);
    }

    [Fact]
    public async Task List_OmitsResourceColumn_WhenResourceFilterApplied()
    {
        StubManifest(Entry("py-http", "Python", "HTTP Trigger"));

        int exit = await InvokeAsync("list", "--resource", "HTTP Trigger");

        Assert.Equal(0, exit);
        string header = _interaction.Lines.First(l => l.StartsWith("TABLE:"));
        Assert.DoesNotContain("Resource", header);
    }

    [Fact]
    public async Task List_OmitsIacColumn_WhenNoEntriesHaveIac()
    {
        StubManifest(Entry("py-http", "Python", "HTTP Trigger", iac: null));

        int exit = await InvokeAsync("list");

        Assert.Equal(0, exit);
        string header = _interaction.Lines.First(l => l.StartsWith("TABLE:"));
        Assert.DoesNotContain("IaC", header);
    }

    [Fact]
    public async Task List_ShowsIacColumn_WhenSomeEntriesHaveIac()
    {
        StubManifest(
            Entry("py-http", "Python", "HTTP Trigger", iac: null),
            Entry("py-bicep", "Python", "HTTP Trigger", iac: "Bicep"));

        int exit = await InvokeAsync("list");

        Assert.Equal(0, exit);
        string header = _interaction.Lines.First(l => l.StartsWith("TABLE:"));
        Assert.Contains("IaC", header);
    }

    [Fact]
    public async Task List_NoResults_ExitsOneWithHint()
    {
        StubManifest(Entry("py", "Python"));

        int exit = await InvokeAsync("list", "--language", "java");

        Assert.Equal(1, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        Assert.Contains(_interaction.Lines,
            l => l.StartsWith("HINT:") && l.Contains("No templates matched"));
    }

    [Fact]
    public async Task List_NoResultsWithSearch_HintMentionsSearchTerm()
    {
        StubManifest(Entry("py", "Python"));

        int exit = await InvokeAsync("list", "--search", "openai");

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines,
            l => l.StartsWith("HINT:") && l.Contains("openai"));
    }

    [Fact]
    public async Task List_ManifestFetchFails_ThrowsGracefulException()
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns<QuickstartManifest>(_ => throw new InvalidOperationException("offline"));

        var ex = await Assert.ThrowsAsync<GracefulException>(() => InvokeAsync("list"));
        Assert.Contains("offline", ex.Message);
    }

    // --- Helpers -------------------------------------------------

    private void StubManifest(params QuickstartEntry[] entries)
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest(entries));
    }

    private static QuickstartEntry Entry(
        string id, string language = "Python", string resource = "HTTP Trigger",
        string? iac = null) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Language = language,
            Resource = resource,
            Iac = iac,
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
        };

    private Task<int> InvokeAsync(params string[] args)
    {
        var cmd = new QuickstartListCommand(_interaction, _manifestClient);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        ParseResult result = root.Parse(args);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return result.InvokeAsync(config);
    }
}
