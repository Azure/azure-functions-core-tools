// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncHostTemplateMetadataReaderTests : IDisposable
{
    private const string HostConfigPlace = "/template/.template.config/func.host.json";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-host-metadata-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public void Read_NoHostFile_ReturnsEmptyMetadata()
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(hostConfigPlace: null, hostData: null);

        metadata.Should().BeSameAs(FuncHostTemplateMetadata.Empty);
    }

    [Fact]
    public void Read_SymbolInfo_ReturnsAliasesAndVisibility()
    {
        const string HostData = """
            {
              "symbolInfo": {
                "Level": { "longName": "log-level", "shortName": "l" },
                "Plain": { }
              }
            }
            """;

        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(HostConfigPlace, HostData);

        metadata.IsHidden.Should().BeFalse();
        metadata.Symbols.Should().BeEquivalentTo(new Dictionary<string, FuncHostSymbolMetadata>
        {
            ["Level"] = new("log-level", "l", IsHidden: false, AlwaysShow: false),
            ["Plain"] = new(LongName: null, ShortName: null, IsHidden: false, AlwaysShow: false),
        });
    }

    [Theory]
    [InlineData("""{ "isHidden": true }""", true, false)]
    [InlineData("""{ "isHidden": "True" }""", true, false)]
    [InlineData("""{ "alwaysShow": true }""", false, true)]
    [InlineData("""{ "alwaysShow": "false" }""", false, false)]
    public void Read_SymbolVisibility_ReadsEachFlagIndependently(string symbol, bool isHidden, bool alwaysShow)
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(HostConfigPlace, $$"""{ "symbolInfo": { "Level": {{symbol}} } }""");

        metadata.Symbols["Level"].IsHidden.Should().Be(isHidden);
        metadata.Symbols["Level"].AlwaysShow.Should().Be(alwaysShow);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Read_EmptyOrNullShortName_ReturnsNoShortName(string shortName)
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(
            HostConfigPlace,
            $$"""{ "symbolInfo": { "Level": { "shortName": {{shortName}} } } }""");

        metadata.Symbols["Level"].ShortName.Should().BeNull();
    }

    [Fact]
    public void Read_HiddenTemplate_ReturnsHidden()
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(HostConfigPlace, """{ "isHidden": true }""");

        metadata.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Read_KeysInOtherCase_AreRecognized()
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(
            HostConfigPlace,
            """{ "IsHidden": true, "SymbolInfo": { "Level": { "LongName": "log-level" } } }""");

        metadata.IsHidden.Should().BeTrue();
        metadata.Symbols["Level"].LongName.Should().Be("log-level");
    }

    [Fact]
    public void Read_UnknownProperties_AreIgnored()
    {
        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(
            HostConfigPlace,
            """{ "usageExamples": [ "--level High" ], "symbolInfo": { "Level": { "longName": "log-level", "usage": "x" } } }""");

        metadata.Symbols["Level"].LongName.Should().Be("log-level");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "isHidden": "sometimes" }""")]
    [InlineData("""{ "symbolInfo": [] }""")]
    [InlineData("""{ "symbolInfo": { "Level": "log-level" } }""")]
    [InlineData("""{ "symbolInfo": { "Level": { "longName": "" } } }""")]
    [InlineData("""{ "symbolInfo": { "Level": { "longName": null } } }""")]
    [InlineData("""{ "symbolInfo": { "Level": { "longName": 42 } } }""")]
    [InlineData("""{ "symbolInfo": { "Level": { "shortName": 42 } } }""")]
    [InlineData("""{ "symbolInfo": { "Level": { "alwaysShow": 1 } } }""")]
    public void Read_MalformedMetadata_Throws(string hostData)
    {
        Action act = () => FuncHostTemplateMetadataReader.Read(HostConfigPlace, hostData);

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_HostFileWithoutCachedContent_Throws()
    {
        Action act = () => FuncHostTemplateMetadataReader.Read(HostConfigPlace, hostData: null);

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public async Task Read_InstalledTemplate_UsesFuncHostFile()
    {
        ITemplateInfo template = await InstallAndGetTemplateAsync(TemplateFixtures.MetadataTemplateIdentity);

        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(template);

        metadata.Symbols.Should().ContainKey("Level").WhoseValue.LongName.Should().Be("log-level");
    }

    [Fact]
    public async Task Read_InstalledTemplateWithOnlyDotnetHostFile_ReturnsEmptyMetadata()
    {
        ITemplateInfo template = await InstallAndGetTemplateAsync(TemplateFixtures.DotnetHostOnlyTemplateIdentity);

        FuncHostTemplateMetadata metadata = FuncHostTemplateMetadataReader.Read(template);

        metadata.Should().BeSameAs(FuncHostTemplateMetadata.Empty);
    }

    [Fact]
    public async Task Read_InstalledTemplateWithInvalidFuncHostFile_Throws()
    {
        ITemplateInfo template = await InstallAndGetTemplateAsync(TemplateFixtures.InvalidHostTemplateIdentity);

        Action act = () => FuncHostTemplateMetadataReader.Read(template);

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_NullTemplate_Throws()
    {
        Action act = () => FuncHostTemplateMetadataReader.Read(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    private async Task<ITemplateInfo> InstallAndGetTemplateAsync(string identity)
    {
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), hive);
        return await TemplateFixtures.GetInstalledTemplateAsync(hive, identity);
    }
}
