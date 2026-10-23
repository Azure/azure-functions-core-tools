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
    private readonly IQuickstartCatalogService _catalogService = Substitute.For<IQuickstartCatalogService>();

    private QuickstartInfoCommand CreateCommand() => new(_interaction, _resolver, _catalogService, new(_interaction));

    private void SetEntries(params QuickstartEntry[] entries) =>
        _catalogService.SearchAsync(Arg.Any<QuickstartCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new QuickstartCatalog(
                entries.Select(entry => new QuickstartCatalogEntry(entry, QuickstartPackageMetadataStatus.Absent, null, null)).ToList(),
                new(QuickstartCatalogOrigin.LocalFile, "test manifest", null)));

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
        SetEntries();

        int exit = await InvokeAsync(CreateCommand(), "nonexistent");

        exit.Should().Be(1);
        _interaction.Lines.Should().Contain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task Info_FormattedOutput_ShowsEntryDetails()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        SetEntries(entry);

        IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
        _resolver.FindProviderForLanguage("Python").Returns(provider);
        provider.GetDisplayLanguage("Python").Returns("Python");

        int exit = await InvokeAsync(CreateCommand(), "http-py");

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("JSON:"));
        _interaction.Lines.Should().Contain(l => l.Contains("http-py"));
        _interaction.Lines.Should().Contain(l => l.Contains("Python"));
        _interaction.Lines.Should().Contain(l => l.Contains("http"));
        _interaction.AllOutput.Should().Contain("Legacy source scaffolder: func quickstart").And.NotContain("Inspect without acquisition:");
    }

    [Fact]
    public async Task Info_Json_EmitsJsonInsteadOfFormatted()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        SetEntries(entry);

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

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Info_PackageMetadata_UsesInitOnlyWithArtifactMetadataAndNeverClaimsReadiness(bool hasArtifact, bool providerAvailable)
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry();
        QuickstartPackageReference reference = new("Fixture.Project", "1.0.0", "https://example.test/index.json",
            "Fixture.Project.Identity", null, hasArtifact ? new("sha256", new('a', 64), "inert") : null, default, default);
        _catalogService.SearchAsync(Arg.Any<QuickstartCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new QuickstartCatalog([new(entry, QuickstartPackageMetadataStatus.Unverified, reference, null)],
                new(QuickstartCatalogOrigin.LocalFile, "fixture", null)));
        if (providerAvailable)
        {
            IQuickstartProvider provider = QuickstartTestHelpers.CreateProvider();
            provider.GetDisplayLanguage("Python").Returns("Python");
            _resolver.FindProviderForLanguage("Python").Returns(provider);
        }
        else
        {
            _resolver.FindProviderForLanguage("Python").Returns((IQuickstartProvider?)null);
        }

        int exit = await InvokeAsync(CreateCommand(), "http-py");

        exit.Should().Be(0, _interaction.AllOutput);
        if (hasArtifact)
        {
            _interaction.AllOutput.Should().Contain("func init <path> --quickstart http-py --dry-run")
                .And.Contain("does not establish acquisition readiness").And.NotContain("Legacy source scaffolder:");
        }
        else
        {
            _interaction.AllOutput.Should().Contain("no usable package acquisition metadata")
                .And.Contain("Legacy source scaffolder: func quickstart");
        }
    }

    [Fact]
    public async Task Info_Json_NoProvider_FallsBackToRawLanguage()
    {
        QuickstartEntry entry = QuickstartTestHelpers.CreateEntry(language: "FSharp");
        SetEntries(entry);

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
        SetEntries(entry);

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
        SetEntries(entry);

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
