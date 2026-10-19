// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeTemplateGenerationContractTests
{
    private const string Configuration = """
        {
          "identity":"New.Native.Function","tags":{"type":"item","language":"JavaScript","func-generation":"native"},
          "defaultName":"NativeFunction",
          "constraints":{
            "host":{"type":"host","args":[{"hostname":"func"}]},
            "stack":{"type":"func-workload","args":"node"}
          }
        }
        """;

    [Fact]
    public void Read_ExplicitNativeDefinitionAndVerifiedArchive_BindsSourceAuthorityWithoutAnIdentityList()
    {
        NativeTemplatePackagePin package = Package();

        var contract = NativeTemplateGenerationContract.Read(Entry(), Snapshot(Configuration), ["FuncTemplate"], package);

        contract.Should().NotBeNull();
        contract!.Stack.Should().Be("node");
        contract.Language.Should().Be("JavaScript");
        contract.Package.Should().BeSameAs(package);
    }

    [Fact]
    public void Read_MarkerAbsent_PreservesLegacyContractWithoutGrantingNativeAuthority()
    {
        var configuration = JsonNode.Parse(Configuration)!;
        configuration["tags"]!.AsObject().Remove("func-generation");

        var contract = NativeTemplateGenerationContract.Read(Entry(), Snapshot(configuration.ToJsonString()), [], null);

        contract.Should().BeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("no-origin")]
    [InlineData("package")]
    [InlineData("version")]
    [InlineData("shared")]
    [InlineData("not-func")]
    [InlineData("mixed-kind")]
    [InlineData("internal")]
    [InlineData("language")]
    [InlineData("identity")]
    [InlineData("mounted-language")]
    public void Read_UnprovenOrMismatchedContract_RefusesRatherThanFallingBack(string scenario)
    {
        NativeTemplatePackagePin? package = Package();
        var entry = Entry();
        string[] types = ["FuncTemplate"];
        switch (scenario)
        {
            case "missing": package = null; break;
            case "no-origin": package = package with { Origin = null }; break;
            case "package": entry = entry with { Package = new("Another.Package", "1.0.0") }; break;
            case "version": entry = entry with { Package = new("Owned.Templates", "2.0.0") }; break;
            case "shared": types = ["FuncTemplate", "Template"]; break;
            case "not-func": types = ["Template"]; break;
            case "mixed-kind": types = ["FuncTemplate", "FuncCliWorkload"]; break;
            case "internal": entry = entry with { DeclaredInternalRole = "python-append-helper" }; break;
            case "language": entry = entry with { Language = null }; break;
            case "identity": entry = entry with { Identity = "Another.Identity" }; break;
            case "mounted-language": entry = entry with { Language = "TypeScript" }; break;
        }

        Action act = () => NativeTemplateGenerationContract.Read(entry, Snapshot(Configuration), types, package);

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("unknown-mode")]
    [InlineData("mode-null")]
    [InlineData("mode-boolean")]
    [InlineData("mode-case")]
    [InlineData("host")]
    [InlineData("stack")]
    [InlineData("alternatives")]
    [InlineData("action")]
    [InlineData("fsharp")]
    [InlineData("default")]
    [InlineData("additional")]
    public void Read_InvalidNativeDefinition_ReportsSpecificAuthoringFailure(string scenario)
    {
        var configuration = JsonNode.Parse(Configuration)!;
        var entry = Entry();
        var snapshot = Snapshot(Configuration);
        switch (scenario)
        {
            case "unknown-mode": configuration["tags"]!["func-generation"] = "custom"; break;
            case "mode-null": configuration["tags"]!["func-generation"] = null; break;
            case "mode-boolean": configuration["tags"]!["func-generation"] = true; break;
            case "mode-case":
                configuration["tags"]!.AsObject().Remove("func-generation");
                configuration["tags"]!["Func-Generation"] = "native";
                break;
            case "host": configuration["constraints"]!["host"]!["args"]![0]!["hostname"] = "dotnetcli"; break;
            case "stack": configuration["constraints"]!.AsObject().Remove("stack"); break;
            case "alternatives": configuration["constraints"]!["stack"]!["args"] = new JsonArray("node", "python"); break;
            case "action": configuration["postActions"] = new JsonArray(new JsonObject { ["actionId"] = "3A7C4B45-1F5D-4A30-959A-51B88E82B5D2" }); break;
            case "fsharp":
                entry = entry with { Language = "F#" };
                configuration["tags"]!["language"] = "F#";
                break;
            case "default": snapshot = snapshot with { DefaultName = null }; break;
            case "additional": configuration["additionalConfigFiles"] = new JsonArray("other.json"); break;
        }
        snapshot = snapshot with { Content = configuration.ToJsonString() };

        Action act = () => NativeTemplateGenerationContract.Read(entry, snapshot, ["FuncTemplate"], Package());

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    public void Read_InvalidJsonRoot_IsAnAuthoringFailure(string configuration)
    {
        Action act = () => NativeTemplateGenerationContract.Read(Entry(), Snapshot(configuration), ["FuncTemplate"], Package());

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("python", "JavaScript")]
    [InlineData("node", "TypeScript")]
    public void Read_AnotherCommandStackOrLanguage_RefusesBorrowedNativeCapability(string stack, string language)
    {
        var project = new TemplateEngineProjectContext(WorkingDirectory.FromExplicit(Path.GetTempPath()), stack, language);

        Action act = () => NativeTemplateGenerationContract.Read(Entry(), Snapshot(Configuration), ["FuncTemplate"], Package(), project);

        act.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*selected canonical stack and language*");
    }

    [Theory]
    [InlineData("""{"type":"item","language":"JavaScript","func-generation":"native","func-generation":"native"}""")]
    [InlineData("""{"type":"item","language":"JavaScript","func-generation":"native","LANGUAGE":"Python"}""")]
    public void Read_DuplicateNativeTags_DoNotBorrowNormalizedEngineMetadata(string tags)
    {
        string configuration = Configuration.Replace(
            """{"type":"item","language":"JavaScript","func-generation":"native"}""", tags, StringComparison.Ordinal);

        Action act = () => NativeTemplateGenerationContract.Read(Entry(), Snapshot(configuration), ["FuncTemplate"], Package());

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("Native_1-Name", true)]
    [InlineData("", false)]
    [InlineData("host", false)]
    [InlineData("HOST", false)]
    [InlineData("1First", false)]
    [InlineData("quoted'name", false)]
    [InlineData("spaces are invalid", false)]
    [InlineData(null, false)]
    public void FunctionNameDiagnostic_UsesFunctionsNameBoundary(string? name, bool valid)
        => (NativeTemplateGenerationContract.FunctionNameDiagnostic(name) is null).Should().Be(valid);

    [Fact]
    public void FunctionNameDiagnostic_EnforcesExactLengthLimit()
    {
        NativeTemplateGenerationContract.FunctionNameDiagnostic(new string('a', 128)).Should().BeNull();
        NativeTemplateGenerationContract.FunctionNameDiagnostic(new string('a', 129)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("src/functions/native.js", true)]
    [InlineData(".func/config.json", false)]
    [InlineData(".FUNC/config.json", false)]
    [InlineData("nested/.func/profile.json", false)]
    [InlineData(".func. /config.json", false)]
    public void ItemOutputDiagnostic_PreservesTheCliConfigurationBoundary(string path, bool permitted)
        => (NativeTemplateGenerationContract.ItemOutputDiagnostic([new(path, "Create")]) is null).Should().Be(permitted);

    [Fact]
    public void Read_NullInputs_AreProgrammerErrors()
    {
        Action entry = () => NativeTemplateGenerationContract.Read(null!, Snapshot(Configuration), [], null);
        Action configuration = () => NativeTemplateGenerationContract.Read(Entry(), null!, [], null);
        Action types = () => NativeTemplateGenerationContract.Read(Entry(), Snapshot(Configuration), null!, null);
        entry.Should().Throw<ArgumentNullException>();
        configuration.Should().Throw<ArgumentNullException>();
        types.Should().Throw<ArgumentNullException>();
    }

    private static TemplateConfigurationSnapshot Snapshot(string content) => new(content, new string('a', 64), TemplateType.Item, [])
    {
        DefaultName = "NativeFunction",
    };

    private static TemplateCatalogEntry Entry() => new("New.Native.Function", "Native function", null, TemplateType.Item, [],
        null, "JavaScript", 0, new("Owned.Templates", "1.0.0"), false, [], null);

    private static NativeTemplatePackagePin Package() => new("Owned.Templates", "1.0.0", new string('b', 64))
    {
        Origin = new BundledTemplateOrigin("Installed.Node", "1.0.0", new string('c', 64), "local-package"),
    };
}
