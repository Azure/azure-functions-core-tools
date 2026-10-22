// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Azure.Functions.Cli.Workloads.Sdk.Tests;

public sealed class BundledTemplatesPackTests
{
    private const string Version = "7.8.9-pack-fixture";
    private const string FixtureId = "Azure.Functions.Cli.Workloads.Tests.BundledTemplates";

    [Fact]
    public void Pack_NodeDefaults_PreservesPhysicalPayloadAndRawInventory()
    {
        using ZipArchive outer = OpenPackage("node", $"Azure.Functions.Cli.Workloads.Node.{Version}.nupkg");

        using ZipArchive inner = AssertBundledArchive(outer, "tools/any/templates/node-basic.nupkg", "Azure.Functions.Templates.Node.Basic");

        PackageTypes(outer).Should().Equal("FuncCliWorkload");
        outer.GetEntry("tools/any/Azure.Functions.Cli.Workloads.Node.dll").Should().NotBeNull();
        TemplateInventory(inner).Select(template => template.Identity).Should().BeEquivalentTo(
            ["Azure.Functions.Node.Basic.JavaScript", "Azure.Functions.Node.Basic.TypeScript"]);
        Read(inner, "content/project-js/package.json").Should().Contain("\"main\": \"src/functions/*.js\"", "\"@azure/functions\": \"^4.0.0\"");
        Read(inner, "content/project-ts/package.json").Should().Contain("\"main\": \"dist/src/functions/*.js\"", "\"build\": \"tsc\"");
        Read(inner, "README.md").Should().NotBeNullOrWhiteSpace();
        inner.GetEntry("LICENSE").Should().NotBeNull();
        inner.GetEntry("content/project-js/.funcignore").Should().NotBeNull();
        inner.GetEntry("content/project-ts/.gitignore").Should().NotBeNull();
        AssertRepositoryMetadata(outer);
        AssertRepositoryMetadata(inner);
    }

    [Theory]
    [InlineData("fixture")]
    [InlineData("fixture-repeat")]
    public void Pack_NonNodeRid_EmbedsDeclaredBytesWithActualMetadataAndCompleteInventory(string directory)
    {
        using ZipArchive outer = OpenPackage(directory, $"{FixtureId}.win-x64.{Version}.nupkg");

        using ZipArchive inner = AssertBundledArchive(outer, "tools/win-x64/templates/controlled-native.nupkg", "Azure.Functions.Cli.Tests.NativeFixture");

        PackageTypes(outer).Should().Equal("FuncCliWorkloadRidPackage");
        Metadata(outer).Element(Metadata(outer).Name.Namespace + "id")!.Value.Should().Be(FixtureId + ".win-x64");
        using var workload = JsonDocument.Parse(outer.GetEntry("workload.json")!.Open());
        workload.RootElement.GetProperty("kind").GetString().Should().Be("workload");
        workload.RootElement.GetProperty("runtimeIdentifier").GetString().Should().Be("win-x64");
        workload.RootElement.GetProperty("entryPoint").GetProperty("assemblyPath").GetString()
            .Should().Be($"tools/win-x64/{FixtureId}.dll");
        TemplateInventory(inner).Should().Equal(
            ("content/item/.template.config/template.json", "Azure.Functions.Cli.Tests.Controlled.Item", "item"),
            ("content/project/.template.config/template.json", "Azure.Functions.Cli.Tests.Controlled.Project", "project"));
        Read(inner, "content/project/source.txt").Should().Be("Controlled first-party project fixture. No executable template code.\n");
        Read(inner, "content/item/source.txt").Should().Be("Controlled first-party item fixture. No executable template code.\n");
        Read(inner, "README.md").Should().Contain("first-party fixture", "not an executable project");
        inner.GetEntry("LICENSE").Should().NotBeNull();
        AssertRepositoryMetadata(outer);
        AssertRepositoryMetadata(inner);
    }

    [Fact]
    public void Pack_TwoOuterOutputsInOneBuild_ReusesSameProducedNativeBytesAndDeclaration()
    {
        const string path = "tools/win-x64/templates/controlled-native.nupkg";
        using ZipArchive first = OpenPackage("fixture", $"{FixtureId}.win-x64.{Version}.nupkg");
        using ZipArchive second = OpenPackage("fixture-repeat", $"{FixtureId}.win-x64.{Version}.nupkg");
        using Stream firstStream = first.GetEntry(path)!.Open();
        using Stream secondStream = second.GetEntry(path)!.Open();

        SHA256.HashData(firstStream).Should().Equal(SHA256.HashData(secondStream));
        Read(first, "bundled-templates.json").Should().Be(Read(second, "bundled-templates.json"));
    }

    [Theory]
    [InlineData("fixture")]
    [InlineData("fixture-repeat")]
    public void Pack_RidImplementation_UsesRequestedConfigurationAndDeclaredRidProducer(string directory)
    {
        using ZipArchive outer = OpenPackage(directory, $"{FixtureId}.win-x64.{Version}.nupkg");
        using Stream assembly = outer.GetEntry($"tools/win-x64/{FixtureId}.dll")!.Open();
        using MemoryStream bytes = new();
        assembly.CopyTo(bytes);
        bytes.Position = 0;
        using PEReader pe = new(bytes);
        MetadataReader reader = pe.GetMetadataReader();
        Dictionary<string, string?> metadata = [];

        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            MemberReference constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Namespace) != "System.Reflection" || reader.GetString(type.Name) != "AssemblyMetadataAttribute")
            {
                continue;
            }

            BlobReader value = reader.GetBlobReader(attribute.Value);
            value.ReadUInt16().Should().Be(1);
            metadata.Add(value.ReadSerializedString()!, value.ReadSerializedString());
        }

        metadata["FixtureConfiguration"].Should().BeEquivalentTo(TestHelpers.Configuration);
        metadata["FixtureRuntimeIdentifier"].Should().Be("win-x64");
    }

    [Fact]
    public void Pack_RidPointer_DoesNotContainNativeAssetsOrDeclaration()
    {
        using ZipArchive pointer = OpenPackage("fixture", $"{FixtureId}.{Version}.nupkg");

        using var workload = JsonDocument.Parse(pointer.GetEntry("workload.json")!.Open());

        PackageTypes(pointer).Should().Equal("FuncCliWorkload");
        workload.RootElement.GetProperty("kind").GetString().Should().Be("rid-pointer");
        workload.RootElement.GetProperty("packages").GetProperty("win-x64").GetString().Should().Be(FixtureId + ".win-x64");
        pointer.GetEntry("bundled-templates.json").Should().BeNull();
        pointer.Entries.Should().NotContain(entry => entry.FullName.EndsWith(".nupkg", StringComparison.Ordinal));
        pointer.Entries.Should().NotContain(entry => entry.FullName.StartsWith("tools/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fixture-interleaved", "7.8.9-pack-fixture")]
    [InlineData("fixture-interleaved-other", "8.9.10-pack-interleaving")]
    public void Pack_TwoVersionsInterleaved_KeepDeclarationsBoundToTheirActualArchive(string directory, string version)
    {
        const string path = "tools/win-x64/templates/controlled-native.nupkg";
        using ZipArchive outer = OpenPackage(directory, $"{FixtureId}.win-x64.{version}.nupkg");
        using var declaration = JsonDocument.Parse(outer.GetEntry("bundled-templates.json")!.Open());
        JsonElement asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Should().ContainSingle().Which;
        using Stream embedded = outer.GetEntry(path)!.Open();
        using MemoryStream bytes = new();
        embedded.CopyTo(bytes);
        bytes.Position = 0;
        using ZipArchive inner = new(bytes, ZipArchiveMode.Read);
        XElement outerMetadata = Metadata(outer);
        XElement innerMetadata = Metadata(inner);

        declaration.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        outerMetadata.Element(outerMetadata.Name.Namespace + "id")!.Value.Should().Be(FixtureId + ".win-x64");
        outerMetadata.Element(outerMetadata.Name.Namespace + "version")!.Value.Should().Be(version);
        innerMetadata.Element(innerMetadata.Name.Namespace + "id")!.Value.Should().Be("Azure.Functions.Cli.Tests.NativeFixture");
        innerMetadata.Element(innerMetadata.Name.Namespace + "version")!.Value.Should().Be(version);
        asset.GetProperty("path").GetString().Should().Be(path);
        asset.GetProperty("packageId").GetString().Should().Be("Azure.Functions.Cli.Tests.NativeFixture");
        asset.GetProperty("version").GetString().Should().Be(version);
        asset.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        PackageTypes(outer).Should().Equal("FuncCliWorkloadRidPackage");
        PackageTypes(inner).Should().Equal("FuncTemplate");
        TemplateInventory(inner).Should().Equal(
            ("content/item/.template.config/template.json", "Azure.Functions.Cli.Tests.Controlled.Item", "item"),
            ("content/project/.template.config/template.json", "Azure.Functions.Cli.Tests.Controlled.Project", "project"));
        asset.GetProperty("templates").EnumerateArray().Select(template => (
            template.GetProperty("path").GetString()!,
            template.GetProperty("identity").GetString()!,
            template.GetProperty("type").GetString()!)).Should().Equal(TemplateInventory(inner));
        AssertRepositoryMetadata(outer);
        AssertRepositoryMetadata(inner);
    }

    private static ZipArchive OpenPackage(string directory, string fileName)
    {
        return ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "bundled-templates-artifacts", directory, fileName));
    }

    private static ZipArchive AssertBundledArchive(ZipArchive outer, string path, string packageId)
    {
        using var declaration = JsonDocument.Parse(outer.GetEntry("bundled-templates.json")!.Open());
        declaration.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        JsonElement asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Should().ContainSingle().Which;
        asset.GetProperty("path").GetString().Should().Be(path);
        asset.GetProperty("packageId").GetString().Should().Be(packageId);
        asset.GetProperty("version").GetString().Should().Be(Version);
        outer.Entries.Where(entry => entry.FullName.EndsWith(".nupkg", StringComparison.Ordinal))
            .Select(entry => entry.FullName).Should().Equal(path);
        using var bytes = new MemoryStream();
        using Stream embedded = outer.GetEntry(path)!.Open();
        embedded.CopyTo(bytes);
        asset.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        var inner = new ZipArchive(new MemoryStream(bytes.ToArray()), ZipArchiveMode.Read);
        XElement metadata = Metadata(inner);
        metadata.Element(metadata.Name.Namespace + "id")!.Value.Should().Be(packageId);
        metadata.Element(metadata.Name.Namespace + "version")!.Value.Should().Be(Version);
        XElement outerMetadata = Metadata(outer);
        metadata.Element(metadata.Name.Namespace + "version")!.Value.Should()
            .Be(outerMetadata.Element(outerMetadata.Name.Namespace + "version")!.Value);
        PackageTypes(inner).Should().Equal("FuncTemplate");
        asset.GetProperty("templates").EnumerateArray().Select(template => (
            template.GetProperty("path").GetString()!,
            template.GetProperty("identity").GetString()!,
            template.GetProperty("type").GetString()!)).Should().Equal(TemplateInventory(inner));
        return inner;
    }

    private static (string Path, string Identity, string Type)[] TemplateInventory(ZipArchive archive)
    {
        return [.. archive.Entries.Where(entry => entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .Select(entry =>
            {
                using var document = JsonDocument.Parse(entry.Open());
                return (entry.FullName, document.RootElement.GetProperty("identity").GetString()!,
                    document.RootElement.GetProperty("tags").GetProperty("type").GetString()!);
            })];
    }

    private static XElement Metadata(ZipArchive archive)
    {
        using Stream stream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        XElement package = XDocument.Load(stream).Root!;
        return package.Element(package.Name.Namespace + "metadata")!;
    }

    private static string[] PackageTypes(ZipArchive archive)
    {
        XElement metadata = Metadata(archive);
        XNamespace ns = metadata.Name.Namespace;
        return [.. metadata.Element(ns + "packageTypes")!.Elements(ns + "packageType").Select(type => type.Attribute("name")!.Value)];
    }

    private static void AssertRepositoryMetadata(ZipArchive archive)
    {
        XElement metadata = Metadata(archive);
        XElement repository = metadata.Element(metadata.Name.Namespace + "repository")!;
        repository.Attribute("type")!.Value.Should().Be("git");
        repository.Attribute("url")!.Value.Should().Be("https://github.com/Azure/azure-functions-core-tools");
        repository.Attribute("commit")!.Value.Should().MatchRegex("^[0-9a-f]{40}$");
        metadata.Element(metadata.Name.Namespace + "license")!.Value.Should().Be("MIT");
    }

    private static string Read(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
