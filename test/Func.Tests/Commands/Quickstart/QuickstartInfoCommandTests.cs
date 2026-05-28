// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public class QuickstartInfoCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartProviderResolver _resolver = Substitute.For<IQuickstartProviderResolver>();
    private readonly IQuickstartManifestService _manifestService = Substitute.For<IQuickstartManifestService>();

    private QuickstartInfoCommand CreateCommand() => new(_interaction, _resolver, _manifestService);

    [Fact]
    public void QuickstartInfoCommand_HasJsonOption()
    {
        QuickstartInfoCommand cmd = CreateCommand();
        Assert.Contains(cmd.Options, o => o.Name == "--json");
    }

    [Fact]
    public void QuickstartInfoCommand_HasTemplateIdArgument()
    {
        QuickstartInfoCommand cmd = CreateCommand();
        Assert.Single(cmd.Arguments);
        Assert.Equal("id", cmd.Arguments[0].Name);
    }

    [Fact]
    public async Task Info_NotFound_ReturnsOne()
    {
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([]));

        int exit = await InvokeAsync(CreateCommand(), "nonexistent");

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("not found"));
    }

    [Fact]
    public async Task Info_FormattedOutput_ShowsEntryDetails()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        _resolver.FindProviderForLanguage("Python").Returns(provider);
        provider.GetDisplayLanguage("Python").Returns("Python");

        int exit = await InvokeAsync(CreateCommand(), "http-py");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("JSON:"));
        Assert.Contains(_interaction.Lines, l => l.Contains("http-py"));
        Assert.Contains(_interaction.Lines, l => l.Contains("Python"));
        Assert.Contains(_interaction.Lines, l => l.Contains("http"));
    }

    [Fact]
    public async Task Info_Json_EmitsJsonInsteadOfFormatted()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        _resolver.FindProviderForLanguage("Python").Returns(provider);
        provider.GetDisplayLanguage("Python").Returns("Python");

        int exit = await InvokeAsync(CreateCommand(), "http-py", "--json");

        Assert.Equal(0, exit);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"id\":\"http-py\"", jsonLine);
        Assert.Contains("\"name\":\"HTTP Trigger\"", jsonLine);
        Assert.Contains("\"language\":\"Python\"", jsonLine);
        Assert.Contains("\"resource\":\"http\"", jsonLine);
        Assert.Contains("\"iac\":\"bicep\"", jsonLine);
    }

    [Fact]
    public async Task Info_Json_NoProvider_FallsBackToRawLanguage()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry(language: "FSharp");
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        _resolver.FindProviderForLanguage("FSharp").Returns((IQuickstartProvider?)null);

        int exit = await InvokeAsync(CreateCommand(), "http-py", "--json");

        Assert.Equal(0, exit);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"language\":\"FSharp\"", jsonLine);
    }

    [Fact]
    public async Task Info_Json_NullIac_DefaultsToNone()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry(iac: null);
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        _resolver.FindProviderForLanguage("Python").Returns(provider);
        provider.GetDisplayLanguage("Python").Returns("Python");

        int exit = await InvokeAsync(CreateCommand(), "http-py", "--json");

        Assert.Equal(0, exit);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"iac\":\"none\"", jsonLine);
    }

    [Fact]
    public async Task Info_CaseInsensitiveIdLookup()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        _resolver.FindProviderForLanguage("Python").Returns(provider);
        provider.GetDisplayLanguage("Python").Returns("Python");

        int exit = await InvokeAsync(CreateCommand(), "HTTP-PY");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("http-py"));
    }

    // --- helpers ---

    private static Task<int> InvokeAsync(QuickstartInfoCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }
}
