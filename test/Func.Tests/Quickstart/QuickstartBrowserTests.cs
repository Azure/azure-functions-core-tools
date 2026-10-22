// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Tests.Commands.Quickstart;
using Azure.Functions.Cli.Tests.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class QuickstartBrowserTests
{
    [Theory]
    [InlineData("Absent")]
    [InlineData("Unverified")]
    public async Task BrowseAsync_ProductionConsole_LiteralCatalogLabelsOpenDetailsWithoutTreatingClaimsAsSelection(string status)
    {
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.Enter);
        stderr.Enqueue(ConsoleKey.Enter);
        for (int i = 0; i < 6; i++) stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        QuickstartCatalogEntry entry = new(QuickstartTestHelpers.CreateEntry(id: "id[bad]", displayName: "[red]Name[/]"),
            Enum.Parse<QuickstartPackageMetadataStatus>(status), null, null);
        IQuickstartCatalogService catalog = Substitute.For<IQuickstartCatalogService>();
        catalog.SearchAsync(Arg.Any<QuickstartCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new QuickstartCatalog([entry], new(QuickstartCatalogOrigin.LocalFile, "fixture", null)));
        QuickstartBrowser browser = new(catalog, new([Stack()]), [], interaction, new(interaction));

        QuickstartBrowseResult result = await browser.BrowseAsync(new(), CancellationToken.None);

        result.Outcome.Should().Be(QuickstartBrowseOutcome.Canceled);
        result.Entry.Should().BeNull();
        stderr.Output.Should().Contain("[red]Name[/] (id[bad])").And.NotContain($"[{status}]");
        stdout.Output.Should().Contain("[red]Name[/]").And.Contain("id[bad]");
    }

    [Theory]
    [InlineData(4, "Back")]
    [InlineData(5, "Canceled")]
    public async Task BrowseAsync_ProductionConsole_EmptyResultsDistinguishBackAndCancel(int down, string expected)
    {
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        for (int i = 0; i < down; i++) stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        IQuickstartCatalogService catalog = Substitute.For<IQuickstartCatalogService>();
        catalog.SearchAsync(Arg.Any<QuickstartCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new QuickstartCatalog([], new(QuickstartCatalogOrigin.LocalFile, "fixture", null)));
        QuickstartBrowser browser = new(catalog, new([Stack()]), [], interaction, new(interaction));

        QuickstartBrowseResult result = await browser.BrowseAsync(new(), CancellationToken.None);

        result.Outcome.ToString().Should().Be(expected);
        result.Entry.Should().BeNull();
        stderr.Output.Should().Contain("No templates match");
    }

    [Fact]
    public async Task BrowseAsync_SingleLegacyResult_RequiresExplicitDetailSelectionAndReturnsToResults()
    {
        ScriptedInteraction interaction = new(true, "first", "Back", "Cancel browsing");
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.Selections.Should().HaveCount(3);
        interaction.Selections[0].Should().Contain("HTTP Trigger (http-py)");
        interaction.AllOutput.Should().Contain("Legacy entry without a package reference");
        interaction.AllOutput.Should().Contain("not proof of single-stack");
        interaction.AllOutput.Should().NotContain("Run:").And.NotContain("SUCCESS:");
        interaction.Selections.SelectMany(choices => choices).Should().NotContain(choice =>
            choice.Contains("Install", StringComparison.OrdinalIgnoreCase) || choice.Contains("Create", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BrowseAsync_Noninteractive_PrintsResultsWithoutPromptsOrOpeningDetails()
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.AllOutput.Should().Contain("http-py").And.Contain("Absent");
        interaction.AllOutput.Should().NotContain("Legacy entry without a package reference");
        interaction.Selections.Should().BeEmpty();
        interaction.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_SelectionWithoutQualificationService_DoesNotOfferCatalogRows()
    {
        ScriptedInteraction interaction = new(true, "first");
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        QuickstartBrowseResult result = await fixture.Browser.BrowseAsync(new()
        {
            AllowSelection = true,
            InstalledQuery = new(Azure.Functions.Cli.Common.WorkingDirectory.FromExplicit(Path.GetTempPath()),
                new("Owner", "1.0.0", "https://fixture.invalid")),
        }, CancellationToken.None);

        result.Outcome.Should().Be(QuickstartBrowseOutcome.Canceled);
        interaction.Selections.Should().BeEmpty();
        interaction.AllOutput.Should().Contain("cannot verify available templates")
            .And.NotContain("http-py");
    }

    [Fact]
    public async Task BrowseAsync_DuplicateIdsAndNames_CanInspectEachDistinctRow()
    {
        ScriptedInteraction interaction = new(true, "second", "Cancel browsing");
        Fixture fixture = new(interaction,
        [
            QuickstartTestHelpers.CreateEntry(shortDescription: "First row"),
            QuickstartTestHelpers.CreateEntry(shortDescription: "Second row"),
        ]);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.Selections[0].Count(choice => choice.StartsWith("HTTP Trigger (http-py)", StringComparison.Ordinal)).Should().Be(2);
        interaction.Selections[0].Distinct(StringComparer.Ordinal).Should().HaveCount(interaction.Selections[0].Length);
        interaction.AllOutput.Should().Contain("Second row").And.Contain("Conflicting");
        interaction.AllOutput.Should().NotContain("First row");
    }

    [Fact]
    public async Task BrowseAsync_EmptyResults_CanEditAndClearAllDiscoveryFilters()
    {
        ScriptedInteraction interaction = new(true,
            "Change search", "HTTP", "Change resource", "HTTP", "Change IaC", "BICEP", "Clear filters", "Back");
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        await fixture.Browser.BrowseAsync(new(Resource: "missing", Iac: "missing", Search: "missing"), CancellationToken.None);

        interaction.AllOutput.Should().Contain("No templates match");
        interaction.AllOutput.Should().Contain("http-py");
        interaction.Inputs.Should().HaveCount(3);
        interaction.Queries.Should().Contain(query => query.Resource == "HTTP" && query.Iac == "BICEP" && query.Search == "HTTP");
        interaction.Queries.Last().Should().Match<QuickstartCatalogQuery>(query =>
            query.Resource == null && query.Iac == null && query.Search == null && query.LegacyStack!.Stack == "python");
    }

    [Fact]
    public async Task BrowseAsync_EmptyInput_ClearsOneFilterAndRetainsCallerLanguage()
    {
        ScriptedInteraction interaction = new(true, "Change search", " ", "Cancel browsing");
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        await fixture.Browser.BrowseAsync(new(Language: "py", Search: "not-found"), CancellationToken.None);

        interaction.AllOutput.Should().Contain("Language: Python");
        interaction.Queries.Last().Search.Should().BeNull();
        interaction.AllOutput.Should().Contain("http-py");
    }

    [Fact]
    public async Task BrowseAsync_InstalledCapabilityAliasesAndLegacyLanguageBridge_FilterWithoutPackageInference()
    {
        ScriptedInteraction interaction = new(false);
        IProjectStack stack = Stack("dotnet", "C#", "csharp");
        stack.WorkerRuntimeAliases.Returns(["dotnet-isolated"]);
        IQuickstartProvider provider = Substitute.For<IQuickstartProvider>();
        provider.Stack.Returns("dotnet");
        provider.ManifestLanguages.Returns(["CSharp"]);
        provider.GetDisplayLanguage("CSharp").Returns("C#");
        provider.ResolveManifestLanguage("C#").Returns("CSharp");
        Fixture fixture = new(interaction,
        [
            QuickstartTestHelpers.CreateEntry(id: "matching", language: "CSharp"),
            QuickstartTestHelpers.CreateEntry(id: "dotnet-looking-package", language: "Python"),
        ], [stack], [provider]);

        await fixture.Browser.BrowseAsync(new(Stack: "DOTNET-ISOLATED", Language: "csharp"), CancellationToken.None);

        interaction.AllOutput.Should().Contain("matching").And.NotContain("dotnet-looking-package");
        interaction.AllOutput.Should().Contain("Language: C#");
    }

    [Fact]
    public async Task BrowseAsync_NoLegacyProvider_UsesInstalledCapabilityLanguageAliases()
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction,
            [QuickstartTestHelpers.CreateEntry(language: "CSharp")], [Stack("dotnet", "C#", "CSharp")], []);

        await fixture.Browser.BrowseAsync(new(Language: "csharp"), CancellationToken.None);

        interaction.AllOutput.Should().Contain("http-py");
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("python", "unknown")]
    public async Task BrowseAsync_UnsupportedCapability_DoesNotLoadCatalog(string stack, string? language)
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, []);

        await fixture.Browser.BrowseAsync(new(Stack: stack, Language: language), CancellationToken.None);

        interaction.AllOutput.Should().Contain("ERROR:");
        interaction.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_NoInstalledStacks_DoesNotLoadCatalog()
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, [], []);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.AllOutput.Should().Contain("func setup");
        interaction.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_MultipleStacksNoninteractive_RequiresExplicitStack()
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, [], [Stack(), Stack("node", "JavaScript", "js")]);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.AllOutput.Should().Contain("explicitly");
        interaction.Queries.Should().BeEmpty();
        interaction.Selections.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_MultipleStacksInteractive_OffersRealCapabilitiesAndCanCancel()
    {
        ScriptedInteraction interaction = new(true, "Cancel browsing");
        Fixture fixture = new(interaction, [], [Stack(), Stack("node", "JavaScript", "js")]);

        await fixture.Browser.BrowseAsync(new(), CancellationToken.None);

        interaction.Selections.Single().Should().Contain("python (python)").And.Contain("node (node)");
        interaction.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_CanceledBeforeLoad_DoesNotPromptOrLoad()
    {
        ScriptedInteraction interaction = new(true);
        Fixture fixture = new(interaction, []);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => fixture.Browser.BrowseAsync(new(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        interaction.Selections.Should().BeEmpty();
        interaction.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseAsync_CanceledAfterPrompt_DoesNotOpenDetailsOrReload()
    {
        using CancellationTokenSource cancellation = new();
        ScriptedInteraction interaction = new(true, "first") { OnSelection = cancellation.Cancel };
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);

        await FluentActions.Awaiting(() => fixture.Browser.BrowseAsync(new(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        interaction.Queries.Should().ContainSingle();
        interaction.AllOutput.Should().NotContain("Legacy entry without");
    }

    [Fact]
    public async Task BrowseAsync_SourceFailure_PropagatesInsteadOfReportingEmptySuccess()
    {
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, []);
        fixture.Source.GetCatalogDocumentAsync(Arg.Any<CancellationToken>())
            .Returns<QuickstartCatalogDocument>(_ => throw new InvalidOperationException("Catalog unavailable"));

        await FluentActions.Awaiting(() => fixture.Browser.BrowseAsync(new(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Catalog unavailable");

        interaction.AllOutput.Should().NotContain("No templates match");
    }

    [Fact]
    public async Task BrowseAsync_CancellationDuringSourceLoad_PropagatesWithoutResults()
    {
        using CancellationTokenSource cancellation = new();
        ScriptedInteraction interaction = new(false);
        Fixture fixture = new(interaction, [QuickstartTestHelpers.CreateEntry()]);
        fixture.Source.GetCatalogDocumentAsync(cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return fixture.Document;
        });

        await FluentActions.Awaiting(() => fixture.Browser.BrowseAsync(new(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        interaction.AllOutput.Should().NotContain("TABLE:");
    }

    [Fact]
    public async Task BrowseAsync_NullRequest_Throws()
    {
        Fixture fixture = new(new(false), []);

        await FluentActions.Awaiting(() => fixture.Browser.BrowseAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_NullDependencies_Throw()
    {
        ScriptedInteraction interaction = new(false);
        IQuickstartCatalogService catalog = Substitute.For<IQuickstartCatalogService>();
        InstalledProjectStackCatalog stacks = new([]);
        QuickstartCatalogRenderer renderer = new(interaction);

        FluentActions.Invoking(() => new QuickstartCatalogRenderer(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new QuickstartBrowser(null!, stacks, [], interaction, renderer)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new QuickstartBrowser(catalog, null!, [], interaction, renderer)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new QuickstartBrowser(catalog, stacks, null!, interaction, renderer)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new QuickstartBrowser(catalog, stacks, [], null!, renderer)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new QuickstartBrowser(catalog, stacks, [], interaction, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registration_ResolvesBrowserWithoutAnInstalledTemplateStore()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IInteractionService>(new TestInteractionService());
        services.AddSingleton(new InstalledProjectStackCatalog([]));
        services.AddQuickstartManifest();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IQuickstartBrowser>().Should().BeOfType<QuickstartBrowser>();
    }

    private static IProjectStack Stack(string id = "python", string language = "Python", string alias = "py")
    {
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns(id);
        stack.DisplayName.Returns(id);
        stack.SupportedLanguages.Returns([language]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { [language] = [alias] });
        return stack;
    }

    private sealed class Fixture
    {
        public Fixture(
            ScriptedInteraction interaction, IReadOnlyList<QuickstartEntry> entries,
            IReadOnlyList<IProjectStack>? stacks = null, IReadOnlyList<IQuickstartProvider>? providers = null)
        {
            Document = new(new(entries), entries.Select(entry => new QuickstartManifestItem(entry, default)).ToList(),
                new(QuickstartCatalogOrigin.LocalFile, "local manifest", null));
            Source.GetCatalogDocumentAsync(Arg.Any<CancellationToken>()).Returns(Document);
            Browser = new(new RecordingCatalog(new QuickstartCatalogService(Source), interaction.Queries),
                new(stacks ?? [Stack()]), providers ?? [], interaction, new(interaction));
        }

        public IQuickstartCatalogSource Source { get; } = Substitute.For<IQuickstartCatalogSource>();

        public QuickstartCatalogDocument Document { get; }

        public QuickstartBrowser Browser { get; }
    }

    private sealed class RecordingCatalog(IQuickstartCatalogService inner, List<QuickstartCatalogQuery> queries) : IQuickstartCatalogService
    {
        public Task<QuickstartCatalog> SearchAsync(QuickstartCatalogQuery query, CancellationToken cancellationToken)
        {
            queries.Add(query);
            return inner.SearchAsync(query, cancellationToken);
        }
    }

    private sealed class ScriptedInteraction(bool interactive, params string[] responses) : TestInteractionService, IInteractionService
    {
        private readonly Queue<string> _responses = new(responses);

        public override bool IsInteractive => interactive;

        public List<string[]> Selections { get; } = [];

        public List<string> Inputs { get; } = [];

        public List<QuickstartCatalogQuery> Queries { get; } = [];

        public Action? OnSelection { get; init; }

        Task<string> IInteractionService.PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] options = [.. choices];
            Selections.Add(options);
            string response = _responses.Dequeue();
            string selected = response switch
            {
                "first" => options[0],
                "second" => options[1],
                _ => options.Single(option => option == response),
            };
            OnSelection?.Invoke();
            return Task.FromResult(selected);
        }

        Task<string> IInteractionService.PromptForInputAsync(string prompt, string? defaultValue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(prompt);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
