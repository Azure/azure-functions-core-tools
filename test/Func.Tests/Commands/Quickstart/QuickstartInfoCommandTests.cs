// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;

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
        cmd.Options.Should().Contain(o => o.Name == "--json");
    }

    [Fact]
    public void QuickstartInfoCommand_HasTemplateIdArgument()
    {
        QuickstartInfoCommand cmd = CreateCommand();
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("id");
    }

    [Fact]
    public async Task Info_NotFound_ReturnsOne()
    {
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([]));

        int exit = await InvokeAsync(CreateCommand(), "nonexistent");

        exit.Should().Be(1);
        _interaction.Lines.Should().Contain(l => l.Contains("not found"));
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

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("JSON:"));
        _interaction.Lines.Should().Contain(l => l.Contains("http-py"));
        _interaction.Lines.Should().Contain(l => l.Contains("Python"));
        _interaction.Lines.Should().Contain(l => l.Contains("http"));
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

        exit.Should().Be(0);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"id\":\"http-py\"");
        jsonLine.Should().Contain("\"name\":\"HTTP Trigger\"");
        jsonLine.Should().Contain("\"language\":\"Python\"");
        jsonLine.Should().Contain("\"resource\":\"http\"");
        jsonLine.Should().Contain("\"iac\":\"bicep\"");
    }

    [Fact]
    public async Task Info_Json_NoProvider_FallsBackToRawLanguage()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry(language: "FSharp");
        _manifestService.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest([entry]));

        _resolver.FindProviderForLanguage("FSharp").Returns((IQuickstartProvider?)null);

        int exit = await InvokeAsync(CreateCommand(), "http-py", "--json");

        exit.Should().Be(0);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"language\":\"FSharp\"");
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

        exit.Should().Be(0);
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"iac\":\"none\"");
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

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.Contains("http-py"));
    }

    // --- helpers ---

    private static Task<int> InvokeAsync(QuickstartInfoCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }
}
