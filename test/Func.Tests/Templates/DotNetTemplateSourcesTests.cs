// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetTemplateSourcesTests
{
    [Fact]
    public async Task ForcedFSharpControlQualification_ActualHttpAndEventGrid_RequireExactSelectedSourceAssociation()
    {
        using ZipArchive archive = OpenArchive();
        DotNetTemplateSources sources = new();
        foreach (string variant in new[] { "HttpTrigger", "EventGridTrigger" })
        {
            ResolvedTemplate selected = Select(archive, $"Azure.Function.FSharp.Isolated.{variant}.3.x", legacy: false);

            NativeTemplateSourceSnapshot? snapshot = await sources.QualifyFSharpControlAsync(selected, CancellationToken.None);

            snapshot.Should().NotBeNull();
            snapshot!.Sources.Should().ContainSingle();
            snapshot.Outputs.Should().ContainSingle().Which.Source.Should().Be(snapshot.Sources[0]);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("owner")]
    [InlineData("decoy")]
    public async Task ForcedFSharpControlQualification_DriftOrMisboundSource_RefusesBeforeForce(string changed)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.FSharp.Isolated.HttpTrigger.3.x", legacy: false, changed);

        Func<Task> qualify = () => new DotNetTemplateSources().QualifyFSharpControlAsync(selected, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task LiteralAndAuxiliaryQualification_ActualCombinedSignalR_BothProofsComposeWithoutLosingEither()
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.CSharp.Isolated.SignalR.3.x", legacy: false);
        DotNetTemplateSources sources = new();

        NativeTemplateSourceSnapshot? literal = await sources.QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["HubName"] = "hub\\quoted\"\t\n入荷" }, CancellationToken.None);
        NativeTemplateSourceSnapshot? auxiliary = await sources.QualifyAuxiliaryAsync(selected, CancellationToken.None);
        NativeTemplateSourceSnapshot? combined = DotNetTemplateSources.CombineAuxiliarySources(literal, auxiliary);

        literal.Should().NotBeNull();
        auxiliary.Should().NotBeNull();
        combined!.Sources.Should().ContainSingle().Which.Should().Be(literal!.Sources[0]);
        combined.Sources.Should().Equal(auxiliary!.Sources);
        combined.Outputs.Should().Equal(literal.Outputs);
        combined.ConfigurationSha256.Should().Be(selected.ConfigurationSha256);
    }

    [Fact]
    public async Task LiteralAndAuxiliaryQualification_KnownPreAuxiliaryLiteralSource_RetainsOlderHelperBehavior()
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.CSharp.Isolated.SignalR.3.x", legacy: false, "olderLiteral");
        DotNetTemplateSources sources = new();

        NativeTemplateSourceSnapshot? literal = await sources.QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["HubName"] = "hub\\archive" }, CancellationToken.None);
        NativeTemplateSourceSnapshot? auxiliary = await sources.QualifyAuxiliaryAsync(selected, CancellationToken.None);

        literal.Should().NotBeNull();
        auxiliary.Should().BeNull("the exact older source keeps its existing fixed helper behavior, without latest name admission");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteralQualification_OriginalUnfixedMySqlTrigger_RefusesEvenPlainValuesRatherThanAddingAnExtraOne(bool explicitTable)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.CSharp.Isolated.MySqlTriggerBinding", legacy: true);
        Dictionary<string, string?> parameters = [];
        if (explicitTable)
        {
            parameters["table"] = "orders";
        }

        Func<Task> qualify = async () => await new DotNetTemplateSources().QualifyLiteralsAsync(selected, parameters, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>().WithMessage("*cannot preserve this binding value*");
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("configuration")]
    [InlineData("identity")]
    [InlineData("output")]
    public void LiteralAndAuxiliaryQualification_MismatchedProofs_RefuseInsteadOfDroppingAQualification(string changed)
    {
        string identity = "Selected.Template";
        string configuration = new('A', 64);
        TemplateEffectSource literalPin = new(identity, "content/Item.cs", new string('B', 64));
        NativeTemplateSourceSnapshot literal = new(identity, configuration, [literalPin], [new("Item.cs", literalPin)]);
        string auxiliaryIdentity = changed == "identity" ? "Decoy.Template" : identity;
        TemplateEffectSource auxiliaryPin = new(auxiliaryIdentity, literalPin.Identity, changed == "hash" ? new string('C', 64) : literalPin.Sha256);
        NativeTemplateSourceSnapshot auxiliary = new(auxiliaryIdentity, changed == "configuration" ? new string('D', 64) : configuration,
            [auxiliaryPin], [new(changed == "output" ? "Other.cs" : "Item.cs", auxiliaryPin)]);

        Action combine = () => DotNetTemplateSources.CombineAuxiliarySources(literal, auxiliary);

        combine.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SourceQualification_ActualNativeArchive_MatchesAllPinnedConfigurationBytes()
    {
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using ZipArchive outer = ZipFile.OpenRead(archive);
        using var bytes = new MemoryStream();
        using (Stream content = outer.GetEntry("tools/any/templates/dotnet.nupkg")!.Open())
        {
            content.CopyTo(bytes);
        }
        bytes.Position = 0;
        using ZipArchive inner = new(bytes);
        DotNetTemplateSources sources = new();
        ZipArchiveEntry[] configurations = [.. inner.Entries.Where(entry => entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal))];
        configurations.Should().HaveCount(38);
        foreach (ZipArchiveEntry entry in configurations)
        {
            using Stream stream = entry.Open();
            using var configBytes = new MemoryStream();
            stream.CopyTo(configBytes);
            using var configuration = JsonDocument.Parse(configBytes.ToArray());
            JsonElement root = configuration.RootElement;
            TemplateCatalogEntry catalog = new(root.GetProperty("identity").GetString()!, root.GetProperty("name").GetString()!, null,
                root.GetProperty("tags").GetProperty("type").GetString() == "project" ? TemplateType.Project : TemplateType.Item,
                [], null, root.GetProperty("tags").GetProperty("language").GetString(), 0,
                new("Azure.Functions.Templates.DotNet", "1.0.0"), false, [], null);
            string hash = Convert.ToHexString(SHA256.HashData(configBytes.ToArray()));

            sources.Matches(catalog, hash).Should().BeTrue(entry.FullName);
            sources.Matches(catalog, new string('0', 64)).Should().BeFalse();
            sources.Matches(catalog with { Package = new("Unqualified.Package", "1.0.0") }, hash).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task LiteralQualification_UnpairedSurrogate_RefusesBeforeLossySourceEncoding(bool legacy, bool high)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.FSharp.Isolated.BlobTrigger.3.x", legacy);
        string value = "orders" + (high ? '\ud800' : '\udc00');

        Func<Task> qualify = async () => await new DotNetTemplateSources().QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["Path"] = value }, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>().WithMessage("*well-formed Unicode*");
    }

    [Fact]
    public async Task LiteralQualification_InvalidArgumentsAndCancellation_AreNotConvertedToSuccessfulQualification()
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.FSharp.Isolated.BlobTrigger.3.x", legacy: false);
        DotNetTemplateSources sources = new();
        Dictionary<string, string?> parameters = [];
        Func<Task> nullSelected = async () => await sources.QualifyLiteralsAsync(null!, parameters, CancellationToken.None);
        Func<Task> nullParameters = async () => await sources.QualifyLiteralsAsync(selected, null!, CancellationToken.None);
        Func<Task> canceled = async () => await sources.QualifyLiteralsAsync(selected, parameters, new CancellationToken(canceled: true));

        await nullSelected.Should().ThrowAsync<ArgumentNullException>();
        await nullParameters.Should().ThrowAsync<ArgumentNullException>();
        await canceled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("CSharp", "SqlTriggerBinding", "table", "[dbo].[orders\\archive]")]
    [InlineData("FSharp", "BlobTrigger.3.x", "Path", "incoming/orders\"archive")]
    public async Task LiteralQualification_AdaptedSource_BindsExactSelectedConfigurationAndSourceWithoutChangingCanonicalValues(
        string language, string variant, string parameter, string value)
    {
        using ZipArchive archive = OpenArchive();
        string identity = $"Azure.Function.{language}.Isolated.{variant}";
        var selected = Select(archive, identity, legacy: false);
        Dictionary<string, string?> parameters = new() { [parameter] = value, ["namespace"] = "User.Namespace" };

        NativeTemplateSourceSnapshot? snapshot = await new DotNetTemplateSources().QualifyLiteralsAsync(
            selected, parameters, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.TemplateIdentity.Should().Be(identity);
        snapshot.ConfigurationSha256.Should().Be(selected.ConfigurationSha256);
        snapshot.Sources.Should().ContainSingle();
        snapshot.Outputs.Should().ContainSingle();
        snapshot.Outputs[0].Source.Should().Be(snapshot.Sources[0]);
        parameters[parameter].Should().Be(value);
        parameters["namespace"].Should().Be("User.Namespace");
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("入荷/é/😀")]
    public async Task LiteralQualification_OlderSource_PlainAndUnicodeValuesRemainUsableWithoutProvenanceManifest(string value)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.FSharp.Isolated.BlobTrigger.3.x", legacy: true);

        NativeTemplateSourceSnapshot? snapshot = await new DotNetTemplateSources().QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["Path"] = value }, CancellationToken.None);

        snapshot.Should().NotBeNull();
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("\"")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\u0007")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    public async Task LiteralQualification_OlderSource_SpecialValuesRefuseWithSelectedPackageUpdateGuidance(string value)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.FSharp.Isolated.BlobTrigger.3.x", legacy: true);

        Func<Task> qualify = async () => await new DotNetTemplateSources().QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["Path"] = value }, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>().WithMessage(
            "*Azure.Functions.Templates.DotNet 1.0.0*cannot preserve*Identify the installed workload that owns these selected templates*");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("derived")]
    [InlineData("owner")]
    [InlineData("decoy")]
    public async Task LiteralQualification_ChangedMappingOrSelectedOwner_RefusesRatherThanUsingAnotherPackage(string change)
    {
        using ZipArchive archive = OpenArchive();
        ResolvedTemplate selected = Select(archive, "Azure.Function.CSharp.Isolated.SqlTriggerBinding", legacy: false, change);

        Func<Task> qualify = async () => await new DotNetTemplateSources().QualifyLiteralsAsync(
            selected, new Dictionary<string, string?> { ["table"] = "orders" }, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>();
    }

    private static ZipArchive OpenArchive()
    {
        string path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using ZipArchive outer = ZipFile.OpenRead(path);
        var bytes = new MemoryStream();
        using Stream content = outer.GetEntry("tools/any/templates/dotnet.nupkg")!.Open();
        content.CopyTo(bytes);
        bytes.Position = 0;
        return new(bytes);
    }

    private static ResolvedTemplate Select(ZipArchive archive, string identity, bool legacy, string? change = null)
    {
        ZipArchiveEntry configuration = archive.Entries.Single(entry =>
        {
            if (!entry.FullName.StartsWith("content/", StringComparison.Ordinal)
                || !entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal))
            {
                return false;
            }
            using Stream stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("identity").GetString() == identity;
        });
        string configurationPath = configuration.FullName;
        ReadOnlyMemory<byte> bytes = Read(legacy ? "provenance/upstream-content/" + configurationPath + ".original" : configurationPath);
        using var config = JsonDocument.Parse(bytes);
        JsonElement root = config.RootElement;
        TemplateCatalogEntry catalog = new(identity, root.GetProperty("name").GetString()!, null, TemplateType.Item,
            [], null, root.GetProperty("tags").GetProperty("language").GetString(), 0,
            new(change == "owner" ? "Decoy.Package" : "Azure.Functions.Templates.DotNet", "1.0.0"), false, [], null)
        {
            ConstraintEvaluation = TemplateConstraintEvaluation.Eligible,
        };
        string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
        return new(catalog, (_, _) => throw new InvalidOperationException("Qualification must not invoke the template."),
            (path, _) => Task.FromResult(
                change == "source" && Path.GetExtension(path) is ".cs" or ".fs"
                || change == "configuration" && path == configurationPath ? new ReadOnlyMemory<byte>([1, 2, 3])
                    : change == "olderLiteral" && path.EndsWith(".cs", StringComparison.Ordinal)
                        ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Read("provenance/upstream-content/" + path).Span).TrimStart('\ufeff'))
                        : Read(legacy ? "provenance/upstream-content/" + path
                            + (path.EndsWith("/template.json", StringComparison.Ordinal) ? ".original" : string.Empty) : path)),
            change == "derived" ? new string('0', 64) : hash, configurationSourceIdentity:
                "selected-package::/" + (change == "decoy" ? "content/Decoy/.template.config/template.json" : configurationPath));

        ReadOnlyMemory<byte> Read(string path)
        {
            using Stream source = archive.GetEntry(path)!.Open();
            using var content = new MemoryStream();
            source.CopyTo(content);
            return content.ToArray();
        }
    }
}
