// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public class QuickstartListCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartProviderResolver _resolver = Substitute.For<IQuickstartProviderResolver>();
    private readonly IQuickstartManifestService _manifestService = Substitute.For<IQuickstartManifestService>();

    private QuickstartListCommand CreateCommand(params IQuickstartProvider[] providers) =>
        new(_interaction, _resolver, _manifestService, providers);

    [Fact]
    public void QuickstartListCommand_HasExpectedOptions()
    {
        QuickstartListCommand cmd = CreateCommand();
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--stack");
        optionNames.Should().Contain("--language");
        optionNames.Should().Contain("--resource");
        optionNames.Should().Contain("--iac");
        optionNames.Should().Contain("--search");
        optionNames.Should().Contain("--json");
    }

    [Fact]
    public void QuickstartListCommand_StackOptionDescription_NoProviders_PointsAtSetup()
    {
        QuickstartListCommand cmd = CreateCommand();
        Option<string?> stackOption = cmd.StackOption;
        string description = stackOption.Description ?? string.Empty;

        description.Should().Contain("Set up a stack");
        description.Should().Contain("func setup --features");
    }

    [Fact]
    public void QuickstartListCommand_StackOptionDescription_ListsInstalledStacks_SortedAndLowercased()
    {
        QuickstartListCommand cmd = CreateCommand(
            QuickstartTestHelpers.CreateProvider(stack: "Python"),
            QuickstartTestHelpers.CreateProvider(stack: "dotnet"),
            QuickstartTestHelpers.CreateProvider(stack: "node"));
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Supported values: dotnet, node, python.");
    }

    [Fact]
    public void QuickstartListCommand_HelpFooterHint_PointsAtWorkloadSearch()
    {
        QuickstartListCommand cmd = CreateCommand();

        (cmd.GetHelpFooterHint() ?? string.Empty).Should().Contain("func workload search --stack");
    }

    [Fact]
    public async Task List_NoProvider_ReturnsOne()
    {
        _resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IQuickstartProvider?)null);

        int exit = await InvokeAsync(CreateCommand(), "--stack", "missing");

        exit.Should().Be(1);
    }

    [Fact]
    public async Task List_Table_RendersEntriesAsTable()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest(
            QuickstartTestHelpers.CreateEntry("http-py", "HTTP Trigger", "Python", "A Python HTTP trigger"),
            QuickstartTestHelpers.CreateEntry("timer-py", "Timer Trigger", "Python", "A Python timer trigger"));

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand());

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain("TABLE: [ID, Name, Description]");
        _interaction.Lines.Should().NotContain(l => l.StartsWith("JSON:"));
    }

    [Fact]
    public async Task List_Json_EmitsJsonInsteadOfTable()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest(
            QuickstartTestHelpers.CreateEntry("http-py", "HTTP Trigger", "Python", "A Python HTTP trigger"));

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand(), "--json");

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("TABLE:"));
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"id\":\"http-py\"");
        jsonLine.Should().Contain("\"name\":\"HTTP Trigger\"");
        jsonLine.Should().Contain("\"description\":\"A Python HTTP trigger\"");
    }

    [Fact]
    public async Task List_Json_EmptyDescription_DefaultsToEmptyString()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest(
            QuickstartTestHelpers.CreateEntry("no-desc", "No Desc Template", "Python", shortDescription: null));

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand(), "--json");

        exit.Should().Be(0);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"description\":\"\"");
    }

    [Fact]
    public async Task List_NoMatchingTemplates_WritesWarning()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest();

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand());

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.Contains("No templates match"));
    }

    [Fact]
    public async Task List_LanguageResolutionFails_ReturnsOne()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest();

        _resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(provider);
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(manifest);
        _resolver.ResolveOrPromptLanguageAsync(Arg.Any<string?>(), provider, manifest, Arg.Any<CancellationToken>())
            .Returns((null, (int?)1));

        int exit = await InvokeAsync(CreateCommand(), "--language", "unknown");

        exit.Should().Be(1);
    }

    // --- helpers ---

    private QuickstartManifest SetupManifest(params QuickstartEntry[] entries)
    {
        var manifest = new QuickstartManifest([.. entries]);
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(manifest);
        return manifest;
    }

    private void SetupResolverSuccess(IQuickstartProvider provider, QuickstartManifest manifest, string manifestLanguage)
    {
        _resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(provider);
        _resolver.ResolveOrPromptLanguageAsync(Arg.Any<string?>(), provider, manifest, Arg.Any<CancellationToken>())
            .Returns((manifestLanguage, (int?)null));
    }

    private static Task<int> InvokeAsync(QuickstartListCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }
}
