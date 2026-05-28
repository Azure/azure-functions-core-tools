// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public class QuickstartListCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartProviderResolver _resolver = Substitute.For<IQuickstartProviderResolver>();
    private readonly IQuickstartManifestService _manifestService = Substitute.For<IQuickstartManifestService>();

    private QuickstartListCommand CreateCommand() => new(_interaction, _resolver, _manifestService);

    [Fact]
    public void QuickstartListCommand_HasExpectedOptions()
    {
        QuickstartListCommand cmd = CreateCommand();
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--stack", optionNames);
        Assert.Contains("--language", optionNames);
        Assert.Contains("--resource", optionNames);
        Assert.Contains("--iac", optionNames);
        Assert.Contains("--search", optionNames);
        Assert.Contains("--json", optionNames);
    }

    [Fact]
    public async Task List_NoProvider_ReturnsOne()
    {
        _resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IQuickstartProvider?)null);

        int exit = await InvokeAsync(CreateCommand(), "--stack", "missing");

        Assert.Equal(1, exit);
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

        Assert.Equal(0, exit);
        Assert.Contains("TABLE: [ID, Name, Description]", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("JSON:"));
    }

    [Fact]
    public async Task List_Json_EmitsJsonInsteadOfTable()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest(
            QuickstartTestHelpers.CreateEntry("http-py", "HTTP Trigger", "Python", "A Python HTTP trigger"));

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand(), "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"id\":\"http-py\"", jsonLine);
        Assert.Contains("\"name\":\"HTTP Trigger\"", jsonLine);
        Assert.Contains("\"description\":\"A Python HTTP trigger\"", jsonLine);
    }

    [Fact]
    public async Task List_Json_EmptyDescription_DefaultsToEmptyString()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest(
            QuickstartTestHelpers.CreateEntry("no-desc", "No Desc Template", "Python", shortDescription: null));

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand(), "--json");

        Assert.Equal(0, exit);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"description\":\"\"", jsonLine);
    }

    [Fact]
    public async Task List_NoMatchingTemplates_WritesWarning()
    {
        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        QuickstartManifest manifest = SetupManifest();

        SetupResolverSuccess(provider, manifest, "Python");

        int exit = await InvokeAsync(CreateCommand());

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("No templates match"));
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

        Assert.Equal(1, exit);
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
