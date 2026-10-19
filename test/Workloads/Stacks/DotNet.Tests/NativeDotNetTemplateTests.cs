// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed partial class NativeDotNetTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-dotnet-").FullName;
    private static readonly Guid _packageAction = new("B17581D1-C5C9-4489-8F0A-004BE667B814");
    private static readonly Guid _restoreAction = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    private static readonly Guid _editorAction = new("84C0DA21-51C8-4541-9940-6CA19AF04EE6");

    private static string OuterArchive => Directory.GetFiles(
        Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Pack_RealPhysicalWorkload_DeclaresExactNativeBytesAndCompleteRawInventory()
    {
        using var outer = new PackageArchiveReader(OuterArchive);
        outer.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncCliWorkload");
        using var declaration = JsonDocument.Parse(outer.GetStream("bundled-templates.json"));
        JsonElement asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Single();
        asset.GetProperty("path").GetString().Should().Be("tools/any/templates/dotnet.nupkg");
        asset.GetProperty("packageId").GetString().Should().Be("Azure.Functions.Templates.DotNet");
        using var content = new MemoryStream();
        outer.GetStream("tools/any/templates/dotnet.nupkg").CopyTo(content);
        Convert.ToHexStringLower(SHA256.HashData(content.ToArray())).Should().Be(asset.GetProperty("sha256").GetString());
        content.Position = 0;
        using var inner = new PackageArchiveReader(content);
        inner.NuspecReader.GetVersion().Should().Be(outer.NuspecReader.GetVersion());
        inner.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncTemplate");
        inner.GetFiles().Should().Contain("LICENSE", "provenance/upstream-sources.json",
            "provenance/Microsoft.Azure.Functions.Worker.ItemTemplates.nuspec.xml",
            "provenance/Microsoft.Azure.Functions.Worker.ProjectTemplates.nuspec.xml");
        JsonElement[] inventory = [.. asset.GetProperty("templates").EnumerateArray()];
        inventory.Should().HaveCount(38);
        inventory.Count(row => row.GetProperty("type").GetString() == "item").Should().Be(36);
        inventory.Count(row => row.GetProperty("type").GetString() == "project").Should().Be(2);
        inventory.Select(row => row.GetProperty("identity").GetString()).Should().OnlyHaveUniqueItems();
        inner.GetFiles().Count(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal)).Should().Be(38);
        foreach (JsonElement row in inventory)
        {
            using var configuration = JsonDocument.Parse(inner.GetStream(row.GetProperty("path").GetString()!));
            row.GetProperty("identity").GetString().Should().Be(configuration.RootElement.GetProperty("identity").GetString());
            row.GetProperty("type").GetString().Should().Be(configuration.RootElement.GetProperty("tags").GetProperty("type").GetString());
        }
    }

    [Theory]
    [InlineData(null, "HubValue")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("quoted\"\\value", "quoted\\\"\\\\value")]
    public async Task NativeSignalR_OptionalHubName_PreservesOmittedPlaceholderAndExplicitValues(string? value, string expected)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == "Azure.Function.CSharp.Isolated.SignalR.3.x");
        Dictionary<string, string?> parameters = new() { ["namespace"] = "Safe.Namespace" };
        if (value is not null)
        {
            parameters["HubName"] = value;
        }
        string output = Path.Combine(_root, "signalr");

        ITemplateCreationResult result = await new TemplateCreator(session.Settings).InstantiateAsync(
            template, "Example", "Example", output, parameters, cancellationToken: CancellationToken.None);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "Example.cs")).Should().Contain($"HubName = \"{expected}\"");
        template.ParameterDefinitions.Single(parameter => parameter.Name == "HubName").DefaultValue.Should().BeNull();
    }

    [Fact]
    public async Task NativeCatalog_FSharpCosmos_RetainsOriginalShortNameAndAddsPublicCompatibilityAlias()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == "Azure.Function.FSharp.Isolated.CosmosDBTrigger.4.x");

        template.ShortNameList.Should().Equal("CosmosDB", "cosmos");
        template.GroupIdentity.Should().Be("Azure.Function.CosmosDBTrigger");
        template.TagsCollection["language"].Should().Be("F#");
        template.TagsCollection["type"].Should().Be("item");
        template.ParameterDefinitions.Single(parameter => parameter.Name == "Connection").DefaultValue.Should().BeEmpty();
        template.ParameterDefinitions.Single(parameter => parameter.Name == "DatabaseName").DefaultValue.Should().Be("databaseName");
        template.ParameterDefinitions.Single(parameter => parameter.Name == "ContainerName").DefaultValue.Should().Be("containerName");
    }

    [Fact]
    public void Pack_AdditionalParameterAliases_OnlyNameItemUserParametersAndPreserveProjectFrameworkAliases()
    {
        using var inner = new PackageArchiveReader(ExtractInnerArchive());
        foreach (string path in inner.GetFiles().Where(path => path.EndsWith("/.template.config/func.host.json", StringComparison.Ordinal)))
        {
            using var host = JsonDocument.Parse(inner.GetStream(path));
            using var configuration = JsonDocument.Parse(inner.GetStream(path.Replace("func.host.json", "template.json", StringComparison.Ordinal)));
            JsonElement symbols = host.RootElement.GetProperty("symbolInfo");
            if (configuration.RootElement.GetProperty("tags").GetProperty("type").GetString() == "project")
            {
                symbols.EnumerateObject().Should().ContainSingle();
                symbols.GetProperty("Framework").GetProperty("longName").GetString().Should().Be("target-framework");
                symbols.GetProperty("Framework").GetProperty("shortName").GetString().Should().Be("tfm");
                symbols.GetProperty("Framework").TryGetProperty("longAliases", out _).Should().BeFalse();
            }
            else
            {
                foreach (JsonProperty symbol in symbols.EnumerateObject())
                {
                    symbol.Name.Should().NotBe("name");
                    configuration.RootElement.GetProperty("symbols").GetProperty(symbol.Name)
                        .GetProperty("type").GetString().Should().Be("parameter");
                    symbol.Value.GetProperty("longAliases").EnumerateArray().Should().ContainSingle();
                    symbol.Value.TryGetProperty("longName", out _).Should().BeFalse();
                    symbol.Value.TryGetProperty("shortName", out _).Should().BeFalse();
                }
            }
        }
    }

    [Theory]
    [InlineData("itemtemplates", "4.0.5590", "C1BBCC22B2B84F1A9EFFB88956E44BFC784A0ECC74DDE3F4204B6B4AD7128A64")]
    [InlineData("projecttemplates", "4.0.5337", "3BA17330B6C903BA159510337FA50942BD426AAF132F5DAC7B949B78AE8B8299")]
    public void Pack_VerifiedUpstreamArchive_PreservesEveryNativeContentByte(string kind, string version, string digest)
    {
        string upstreamPath = Path.Combine(AppContext.BaseDirectory, "upstream-dotnet-inputs",
            $"microsoft.azure.functions.worker.{kind}.{version}.nupkg");
        SHA256.HashData(File.ReadAllBytes(upstreamPath)).Should().Equal(Convert.FromHexString(digest));
        using var upstream = new ZipArchive(File.OpenRead(upstreamPath));
        using var inner = new PackageArchiveReader(ExtractInnerArchive());
        using var adaptations = JsonDocument.Parse(inner.GetStream("provenance/supporting-types.json"));
        var adapted = adaptations.RootElement.EnumerateArray()
            .ToDictionary(row => row.GetProperty("path").GetString()!, row => row.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        using var auxiliaries = JsonDocument.Parse(inner.GetStream("provenance/auxiliary-identities.json"));
        foreach (JsonElement row in auxiliaries.RootElement.EnumerateArray())
        {
            adapted.Add(row.GetProperty("path").GetString()!, row.GetProperty("sha256").GetString()!);
        }
        using var models = JsonDocument.Parse(inner.GetStream("provenance/nullable-models.json"));
        foreach (JsonElement row in models.RootElement.EnumerateArray())
        {
            adapted[row.GetProperty("path").GetString()!] = row.GetProperty("sha256").GetString()!;
        }
        using var literals = JsonDocument.Parse(inner.GetStream("provenance/binding-literals.json"));
        foreach (JsonElement literal in literals.RootElement.EnumerateArray())
        {
            string path = literal.GetProperty("path").GetString()!;
            adapted[path] = literal.GetProperty("sha256").GetString()!;
            adapted[path[..path.LastIndexOf('/')] + "/.template.config/template.json"] = string.Empty;
        }
        ZipArchiveEntry[] sourceFiles = [.. upstream.Entries.Where(entry => entry.FullName.StartsWith("content/", StringComparison.Ordinal)
            && entry.Name.Length > 0)];
        sourceFiles.Should().NotBeEmpty();
        foreach (ZipArchiveEntry source in sourceFiles)
        {
            using Stream expected = source.Open();
            string actualPath = adapted.ContainsKey(source.FullName) ? "provenance/upstream-content/" + source.FullName
                + (source.FullName.EndsWith("/template.json", StringComparison.Ordinal) ? ".original" : string.Empty) : source.FullName;
            using Stream actual = inner.GetStream(actualPath);
            SHA256.HashData(actual).Should().Equal(SHA256.HashData(expected), source.FullName);
            if (adapted.TryGetValue(source.FullName, out string? pinnedHash) && pinnedHash.Length > 0)
            {
                using Stream original = source.Open();
                Convert.ToHexString(SHA256.HashData(original)).Should().Be(pinnedHash);
                using Stream native = inner.GetStream(source.FullName);
                using StreamReader reader = new(native);
                reader.ReadToEnd().Should().NotContain("\npublic class ToDoItem\n");
            }
        }
    }

    [Theory]
    [InlineData("CSharp", "C#")]
    [InlineData("FSharp", "F#")]
    public async Task NativeLiteralParameters_QuotesBackslashesAndControls_AreEncodedOnlyAtAuthoredLiteralSites(
        string language, string tag)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == $"Azure.Function.{language}.Isolated.BlobTrigger.3.x");
        string output = Path.Combine(_root, language + "-literals");
        TemplateCreator creator = new(session.Settings);
        Dictionary<string, string?> parameters = new()
        {
            ["namespace"] = "Custom.Functions",
            ["Path"] = "incoming/orders\\archive\"\t\n\r\b\f\u0007\u000b\u0085\u2028\u2029/入荷/😀",
            ["Connection"] = "Connection\\value\"",
        };

        ITemplateCreationResult created = await creator.InstantiateAsync(template, "Example", "Example", output, parameters,
            cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        string source = File.ReadAllText(Path.Combine(output, "Example" + (tag == "C#" ? ".cs" : ".fs")));
        source.Should().Contain("namespace Custom.Functions");
        source.Should().Contain("\"incoming/orders\\\\archive\\\"\\t\\n\\r\\b\\f\\u0007\\u000b\\u0085\\u2028\\u2029/入荷/😀/{name}\"");
        source.Should().Contain("\"Connection\\\\value\\\"\"");
        source.Should().NotContain("__FuncBinding_");
        parameters["Path"].Should().Be("incoming/orders\\archive\"\t\n\r\b\f\u0007\u000b\u0085\u2028\u2029/入荷/😀");
    }

    [Fact]
    public async Task NativeLiteralParameters_CompleteReviewedInventory_UsesDerivedFormsWithoutChangingTypedParameters()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        using var inner = new PackageArchiveReader(ExtractInnerArchive());
        using var inventory = JsonDocument.Parse(inner.GetStream("provenance/binding-literals.json"));
        int parameterCount = 0;
        foreach (JsonElement row in inventory.RootElement.EnumerateArray())
        {
            ITemplateInfo template = templates.Single(item => item.Identity == row.GetProperty("identity").GetString());
            string path = row.GetProperty("path").GetString()!;
            using var configuration = JsonDocument.Parse(inner.GetStream(path[..path.LastIndexOf('/')] + "/.template.config/template.json"));
            JsonElement symbols = configuration.RootElement.GetProperty("symbols");
            Dictionary<string, string?> parameters = new() { ["namespace"] = "Safe.Namespace" };
            foreach (JsonElement mapping in row.GetProperty("parameters").EnumerateArray())
            {
                string name = mapping[0].GetString()!;
                template.ParameterDefinitions.Should().Contain(parameter => parameter.Name == name);
                symbols.GetProperty(name).TryGetProperty("replaces", out _).Should().BeFalse();
                JsonElement derived = symbols.GetProperty("FuncBinding_" + name);
                derived.GetProperty("type").GetString().Should().Be("derived");
                derived.GetProperty("valueSource").GetString().Should().Be(name);
                derived.GetProperty("valueTransform").GetString().Should().Be(
                    template.TagsCollection["language"] == "C#" ? "csharpStringContent" : "fsharpStringContent");
                parameters[name] = "plain\\quote\"\t\n入荷";
                parameterCount++;
            }
            string output = Path.Combine(_root, template.Identity);
            ITemplateCreationResult result = await new TemplateCreator(session.Settings).InstantiateAsync(
                template, "Example", "Example", output, parameters, cancellationToken: CancellationToken.None);
            result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
            string source = File.ReadAllText(Path.Combine(output, Path.GetFileName(path)
                .Replace(configuration.RootElement.GetProperty("sourceName").GetString()!, "Example", StringComparison.Ordinal)));
            source.Should().NotContain("__FuncBinding_");
            source.Should().Contain("plain\\\\quote\\\"\\t\\n入荷");
            source.Should().Contain("namespace Safe.Namespace");
            string defaults = output + "-defaults";
            ITemplateCreationResult defaultResult = await new TemplateCreator(session.Settings).InstantiateAsync(
                template, "Example", "Example", defaults, new Dictionary<string, string?> { ["namespace"] = "Safe.Namespace" },
                cancellationToken: CancellationToken.None);
            defaultResult.Status.Should().Be(CreationResultStatus.Success, defaultResult.ErrorMessage);
            File.ReadAllText(Path.Combine(defaults, Path.GetFileName(path)
                .Replace(configuration.RootElement.GetProperty("sourceName").GetString()!, "Example", StringComparison.Ordinal)))
                .Should().NotContain("__FuncBinding_");
        }
        inventory.RootElement.GetArrayLength().Should().Be(23);
        parameterCount.Should().Be(48);
    }

    [Fact]
    public async Task NativeCatalog_RealArchive_PreservesRawLanguageRecordsAndNativeParameters()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        templates.Should().HaveCount(38);
        ITemplateInfo[] items = [.. templates.Where(template => template.TagsCollection["type"] == "item")];
        items.Count(template => template.TagsCollection["language"] == "C#").Should().Be(29);
        items.Count(template => template.TagsCollection["language"] == "F#").Should().Be(7);
        items.Select(template => template.GroupIdentity).Distinct(StringComparer.Ordinal).Should().HaveCount(29);
        ITemplateInfo http = items.Single(template => template.Identity == "Azure.Function.CSharp.Isolated.HttpTrigger.3.x");
        http.ShortNameList.Should().Contain("http");
        ITemplateParameter access = http.ParameterDefinitions.Single(parameter => parameter.Name == "AccessRights");
        access.Choices!.Keys.Should().BeEquivalentTo("Function", "Anonymous", "Admin");
        access.DefaultValue.Should().Be("Function");
        http.ParameterDefinitions.Should().Contain(parameter => parameter.Name == "namespace");
        ITemplateInfo project = templates.Single(template => template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        project.ParameterDefinitions.Single(parameter => parameter.Name == "Framework").Choices!.Keys.Should().Contain("net10.0");
        (project as ITemplateInfoHostJsonCache)!.HostData.Should().Contain("target-framework", "tfm");
    }

    [Theory]
    [InlineData("DurableFunctionsEntityClass.1.x")]
    [InlineData("DurableFunctionsEntityFunction.1.x")]
    [InlineData("DurableFunctionsOrchestration.1.x")]
    [InlineData("SignalR.3.x")]
    [InlineData("DaprPublishOutputBinding")]
    [InlineData("DaprServiceInvocationTrigger")]
    public async Task NativeAuxiliaryItems_RequestedNames_OwnEveryFunctionAndSupportingType(string variant)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == "Azure.Function.CSharp.Isolated." + variant);
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "auxiliary");
        Directory.CreateDirectory(output);
        string custom = Path.Combine(output, "Custom.cs");
        const string userSource = "namespace Custom.Functions; public class Counter { } public class MyConnectionInfo { } "
            + "public class InvokeOutputBinding { } public class DaprTopicTriggerFuncApp { } "
            + "public class FirstConnectionInfo { } public class FirstInvoke { } public class FirstTopicTrigger { }";
        File.WriteAllText(custom, userSource);
        foreach (string name in new[] { "First", "Second" })
        {
            var parameters = new Dictionary<string, string?> { ["namespace"] = "Custom.Functions" };
            if (variant == "SignalR.3.x")
            {
                parameters["HubName"] = "customHub";
            }

            ITemplateCreationResult preview = await creator.InstantiateAsync(template, name, name, output, parameters,
                dryRun: true, cancellationToken: CancellationToken.None);

            preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
            File.Exists(Path.Combine(output, name + ".cs")).Should().BeFalse();
            ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output, parameters,
                cancellationToken: CancellationToken.None);
            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string source = File.ReadAllText(Path.Combine(output, name + ".cs"));
            source.Should().Contain("namespace Custom.Functions");
            switch (variant)
            {
                case "DurableFunctionsEntityClass.1.x":
                    source.Should().ContainAll($"public class {name} : TaskEntity<int>", $"public {name}(ILogger<{name}> logger)",
                        $"[Function(nameof({name}))]");
                    source.Should().NotContain("Counter");
                    break;
                case "DurableFunctionsEntityFunction.1.x":
                    source.Should().Contain($"[Function(nameof({name}))]").And.NotContain("\"Counter\"");
                    break;
                case "DurableFunctionsOrchestration.1.x":
                    source.Should().ContainAll($"[Function(nameof({name}))]", $"public static string {name}SayHello(",
                        $"[Function(\"{name}-SayHello\")]", $"GetLogger(\"{name}-SayHello\")",
                        $"[Function(\"{name}-HttpStart\")]", $"nameof({name}));");
                    source.Split($"CallActivityAsync<string>(\"{name}-SayHello\"").Should().HaveCount(4);
                    break;
                case "SignalR.3.x":
                    source.Should().ContainAll($"[Function(\"{name}\")]", $"CreateLogger(\"{name}\")",
                        $"        [SignalRConnectionInfoInput(HubName = \"customHub\")] {name}ConnectionInfo connectionInfo)",
                        $"    public class {name}ConnectionInfo");
                    source.Should().NotContain("MyConnectionInfo").And.NotContain("\"negotiate\"");
                    break;
                case "DaprPublishOutputBinding":
                    source.Should().ContainAll($"[Function(\"{name}\")]", $"GetLogger(\"{name}\")",
                        $"public static void {name}TopicTrigger(", $"[Function(\"{name}-TopicTrigger\")]",
                        $"GetLogger(\"{name}-TopicTrigger\")", "[DaprPublishOutput(PubSubName = \"pubsub\", Topic = \"A\")]",
                        "[DaprTopicTrigger(\"pubsub\", Topic = \"A\")]");
                    source.Should().NotContain("DaprTopicTriggerFuncApp");
                    break;
                case "DaprServiceInvocationTrigger":
                    source.Should().ContainAll($"[Function(\"{name}\")]", $"public static async Task<InvokeMethodParameters> {name}Invoke(",
                        $"[Function(\"{name}-Invoke\")]", $"GetLogger(\"{name}-Invoke\")",
                        $"Route = \"{name}/invoke/{{appId}}/{{methodName}}\"",
                        "AppId = \"{appId}\", MethodName = \"{methodName}\"",
                        $"--method {name}", $"/api/{name}/invoke/functionapp/{name}");
                    source.Should().NotContain("InvokeOutputBinding").And.NotContain("{yourFunctionName}");
                    break;
            }
            File.ReadAllText(custom).Should().Be(userSource);
        }
    }

    [Theory]
    [InlineData("MySqlOutputBinding", "WriteTodos", "Task<WriteTodosItem>", "orders")]
    [InlineData("MySqlTriggerBinding", "WatchTodos", "MySqlChange<WatchTodosItem>", "orders")]
    [InlineData("SqlOutputBinding", "ToDoItem", "Task<ToDoItemItem>", "orders")]
    [InlineData("SqlTriggerBinding", "WatchSql", "SqlChange<WatchSqlItem>", "orders")]
    public async Task NativeDatabaseItem_SupportingType_IsScopedToTheNewFunction(
        string variant, string name, string expectedReference, string expectedTable)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.CSharp.Isolated.{variant}");
        string output = Path.Combine(_root, variant);
        Directory.CreateDirectory(output);
        string custom = Path.Combine(output, "Custom.cs");
        string original = "namespace Custom.Functions; public class "
            + (name == "ToDoItem" ? "ExistingDto" : "ToDoItem") + " { } public class WriteTodosItem { }";
        File.WriteAllText(custom, original);
        TemplateCreator creator = new(session.Settings);
        Dictionary<string, string?> parameters = new()
        {
            ["namespace"] = "Custom.Functions",
            ["table"] = "orders",
            ["connectionStringSetting"] = "DatabaseConnection",
        };

        ITemplateCreationResult preview = await creator.InstantiateAsync(template, name, name, output, parameters,
            dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        Directory.GetFiles(output).Should().ContainSingle().Which.Should().Be(custom);
        ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        string source = File.ReadAllText(Path.Combine(output, name + ".cs"));
        source.Should().ContainAll("namespace Custom.Functions;", $"public class {name}", $"[Function(\"{name}\")]",
            $"    public class {name}Item", expectedReference, $"\"{expectedTable}\"", "\"DatabaseConnection\"");
        source.Should().NotContain($"\npublic class {name}Item\n");
        File.ReadAllText(custom).Should().Be(original);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("Sql")]
    public async Task NativeDatabaseItems_RepeatedOutputAndTrigger_KeepSupportingTypesInsideEachNewFunction(string provider)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, provider);
        foreach (string kind in new[] { "Output", "Trigger" })
        {
            ITemplateInfo template = templates.Single(item => item.Identity == $"Azure.Function.CSharp.Isolated.{provider}{kind}Binding");
            foreach (string suffix in new[] { "First", "Second" })
            {
                string name = kind + suffix;
                ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output,
                    new Dictionary<string, string?> { ["namespace"] = "Shared.Functions" }, cancellationToken: CancellationToken.None);

                created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
                string source = File.ReadAllText(Path.Combine(output, name + ".cs"));
                source.Should().ContainAll($"public class {name}\n", $"    public class {name}Item\n",
                    $"<{name}Item>", $"[Function(\"{name}\")]");
                source.Should().NotContain("ToDoItem");
            }
        }
        Directory.GetFiles(output, "*.cs").Should().HaveCount(4);
    }

    [Theory]
    [InlineData("MySql", "Input", "object", "table1")]
    [InlineData("MySql", "Output", "table", "table1")]
    [InlineData("MySql", "Trigger", "table", "table1")]
    [InlineData("Sql", "Input", "object", "[dbo].[table1]")]
    [InlineData("Sql", "Output", "table", "[dbo].[table1]")]
    [InlineData("Sql", "Trigger", "table", "[dbo].[table1]")]
    public async Task NativeDatabaseItems_TableParameter_MatchesTheCompleteBindingValue(
        string provider, string kind, string parameter, string defaultValue)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == $"Azure.Function.CSharp.Isolated.{provider}{kind}Binding");
        TemplateCreator creator = new(session.Settings);
        foreach (bool explicitValue in new[] { false, true })
        {
            Dictionary<string, string?> arguments = new() { ["namespace"] = "Custom.Functions" };
            string table = defaultValue;
            if (explicitValue)
            {
                table = "sales.orders";
                arguments[parameter] = table;
                arguments["connectionStringSetting"] = "DatabaseConnection";
            }
            string output = Path.Combine(_root, explicitValue.ToString());

            ITemplateCreationResult created = await creator.InstantiateAsync(template, "Example", "Example", output,
                arguments, cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string source = File.ReadAllText(Path.Combine(output, "Example.cs"));
            string command = kind == "Input" ? "SELECT * FROM " + table : table;
            source.Should().Contain($"[{provider}{kind}(\"{command}\",");
            source.Should().Contain("namespace Custom.Functions;");
            source.Should().Contain(explicitValue ? "\"DatabaseConnection\"" : "\"\"");
        }
    }

    [Fact]
    public async Task NativeSqlTrigger_Serializer_IsAvailableWithoutAnUndeclaredPackage()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(item => item.Identity == "Azure.Function.CSharp.Isolated.SqlTriggerBinding");
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "sql-serializer");

        ITemplateCreationResult created = await creator.InstantiateAsync(template, "WatchSql", "WatchSql", output,
            new Dictionary<string, string?> { ["namespace"] = "Custom.Functions" }, cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        string source = File.ReadAllText(Path.Combine(output, "WatchSql.cs"));
        source.Should().ContainAll("using System.Text.Json;", "JsonSerializer.Serialize(changes)");
        source.Should().NotContain("Newtonsoft").And.NotContain("JsonConvert");
        created.CreationEffects!.CreationResult.PostActions.Where(action => action.ActionId == _packageAction)
            .Select(action => action.Args["reference"]).Should().Equal("Microsoft.Azure.Functions.Worker.Extensions.Sql");
    }

    [Theory]
    [InlineData("CSharp", ".csproj", "Program.cs")]
    [InlineData("FSharp", ".fsproj", "Program.fs")]
    public async Task NativeProject_ExplicitSupportedFramework_RendersAndRetainsEvaluatedDependencyActions(
        string language, string extension, string program)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        string output = Path.Combine(_root, language);
        var parameters = new Dictionary<string, string?> { ["Framework"] = "net10.0" };
        TemplateCreator creator = new(session.Settings);

        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters, dryRun: true,
            cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        Directory.Exists(output).Should().BeFalse();
        IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
        actions.Should().Contain(action => action.ActionId == _restoreAction);
        actions.Should().OnlyContain(action => action.ActionId == _packageAction || action.ActionId == _restoreAction);
        if (language == "FSharp")
        {
            actions.Where(action => action.ActionId == _packageAction).Select(action => action.Args["reference"])
                .Should().Equal("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "Microsoft.Azure.Functions.Worker.Extensions.Http");
        }
        ITemplateCreationResult created = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "Sample" + extension)).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        File.Exists(Path.Combine(output, program)).Should().BeTrue();
        File.Exists(Path.Combine(output, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "local.settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, "_gitignore")).Should().BeFalse();
        File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse("raw native rendering is not CLI configuration finalization");
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("CSharp", ".csproj", ".cs", true)]
    [InlineData("FSharp", ".fsproj", ".fs", false)]
    public async Task NativeProjectAndHttp_ActualMsBuildCompileEvaluation_CharacterizesInheritedFSharpInclusionGap(
        string language, string projectExtension, string itemExtension, bool itemParticipates)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo project = templates.Single(template =>
            template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        ITemplateInfo http = templates.Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        string output = Path.Combine(_root, "compilation-" + language);
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult createdProject = await creator.InstantiateAsync(project, "Sample", "Sample", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: CancellationToken.None);
        createdProject.Status.Should().Be(CreationResultStatus.Success);
        string projectPath = Path.GetFullPath(Path.Combine(output, createdProject.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        Path.GetExtension(projectPath).Should().Be(projectExtension);
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateProjectContext evaluated = await new DotNetTemplateProjectPreparation(runner)
            .ReadAsync(projectPath, CancellationToken.None);
        evaluated.TargetFramework.Should().Be("net10.0");
        ITemplateCreationResult createdItem = await creator.InstantiateAsync(http, "Greeting", "Greeting", output,
            new Dictionary<string, string?> { ["namespace"] = evaluated.RootNamespace }, cancellationToken: CancellationToken.None);
        createdItem.Status.Should().Be(CreationResultStatus.Success);
        File.Exists(Path.Combine(output, "Greeting" + itemExtension)).Should().BeTrue();
        StringBuilder nativeOutput = new();

        await runner.RunStreamingAsync(["msbuild", projectPath, "-nologo", "-getItem:Compile"], output,
            line => nativeOutput.AppendLine(line), null, CancellationToken.None);

        using var items = JsonDocument.Parse(nativeOutput.ToString());
        string?[] compile = [.. items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())];
        compile.Contains("Greeting" + itemExtension, StringComparer.Ordinal).Should().Be(itemParticipates,
            "the independently exercised pinned dotnet-new baseline includes C# by glob but leaves the F# project with only Program.fs");
        if (!itemParticipates)
        {
            compile.Should().ContainSingle().Which.Should().Be("Program.fs",
                "this is a characterized gap, not evidence that the rendered F# HTTP function compiles");
        }

        File.Exists(Path.Combine(output, "obj", "project.assets.json")).Should().BeFalse("compile-item evaluation does not restore dependencies");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeFSharpHttp_QualifiedCompileRepair_BuildMetadataContainsGeneratedFunction()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo projectTemplate = templates.Single(template =>
            template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.FSharp.Isolated.3.x");
        ITemplateInfo itemTemplate = templates.Single(template => template.Identity == "Azure.Function.FSharp.Isolated.HttpTrigger.3.x");
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "repaired-native-fsharp");
        ITemplateCreationResult createdProject = await creator.InstantiateAsync(projectTemplate, "RepairedApp", "RepairedApp", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: CancellationToken.None);
        createdProject.Status.Should().Be(CreationResultStatus.Success);
        string project = Path.GetFullPath(Path.Combine(output, createdProject.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateProjectContext evaluated = await new DotNetTemplateProjectPreparation(runner).ReadAsync(project, CancellationToken.None);
        string qualification = Path.Combine(_root, "native-source-expansion");
        Dictionary<string, string?> arguments = new() { ["namespace"] = evaluated.RootNamespace, ["AccessRights"] = "Anonymous" };
        ITemplateCreationResult expanded = await creator.InstantiateAsync(itemTemplate, "RepairedHttp", "RepairedHttp", qualification,
            arguments, cancellationToken: CancellationToken.None);
        expanded.Status.Should().Be(CreationResultStatus.Success);
        string generated = Path.Combine(output, "RepairedHttp.fs");
        string expectedSource = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(qualification, "RepairedHttp.fs"))));
        FSharpCompileUpdater updater = new(runner, new());
        FSharpCompileUpdatePlan plan = await updater.PlanAsync(project, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project))),
            [Path.Combine(output, "Program.fs")], [new(generated, expectedSource)], CancellationToken.None);
        File.Exists(generated).Should().BeFalse();

        ITemplateCreationResult createdItem = await creator.InstantiateAsync(itemTemplate, "RepairedHttp", "RepairedHttp", output,
            arguments, cancellationToken: CancellationToken.None);
        createdItem.Status.Should().Be(CreationResultStatus.Success);
        await updater.ApplyAsync(plan, CancellationToken.None);

        StringBuilder evaluatedItems = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], output,
            line => evaluatedItems.AppendLine(line), null, CancellationToken.None);
        using var compile = JsonDocument.Parse(evaluatedItems.ToString());
        compile.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()).Should().Equal("RepairedHttp.fs", "Program.fs");
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("The repository NuGet.Config was not found.");
        }

        string configuration = Path.Combine(repository.FullName, "NuGet.Config");
        File.Copy(configuration, Path.Combine(output, "NuGet.Config"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        DotNetTemplateDependencyExecutor dependencies = new(runner);
        IPostAction package = createdItem.CreationEffects!.CreationResult.PostActions.Single(action => action.ActionId == _packageAction);
        await dependencies.AddPackageReferenceAsync(project,
            new(package.Args["reference"], package.Args["version"], package.Args["projectFileExtensions"]), timeout.Token);
        await dependencies.RestoreAsync(project, timeout.Token);
        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"], output, timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString())
            .Should().Contain("RepairedHttp", "the actual native F# HTTP file must participate in the built function metadata");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeAuxiliaryItems_DeclaredDependencies_BuildWithUniqueFunctionNamesAndHttpRoutes()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "auxiliary-build");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        ITemplateInfo projectTemplate = templates.Single(template =>
            template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        ITemplateCreationResult projectResult = await creator.InstantiateAsync(projectTemplate, "Sample", "Sample", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: timeout.Token);
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        List<IPostAction> actions = [.. projectResult.CreationEffects!.CreationResult.PostActions];
        List<string> expectedNames = [];
        Dictionary<string, string> expectedRoutes = new(StringComparer.Ordinal);
        (string Variant, string Prefix, string[] Suffixes)[] cases =
        [
            ("DurableFunctionsEntityClass.1.x", "EntityClass", [""]),
            ("DurableFunctionsEntityFunction.1.x", "EntityFunction", [""]),
            ("DurableFunctionsOrchestration.1.x", "Orchestrate", ["", "-SayHello", "-HttpStart"]),
            ("SignalR.3.x", "Negotiate", [""]),
            ("DaprPublishOutputBinding", "Publish", ["", "-TopicTrigger"]),
            ("DaprServiceInvocationTrigger", "Service", ["", "-Invoke"]),
        ];
        foreach ((string variant, string prefix, string[] suffixes) in cases)
        {
            ITemplateInfo template = templates.Single(item => item.Identity == "Azure.Function.CSharp.Isolated." + variant);
            foreach (string ordinal in new[] { "First", "Second" })
            {
                string name = prefix + ordinal;
                ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output,
                    new Dictionary<string, string?> { ["namespace"] = "Sample" }, cancellationToken: timeout.Token);
                created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
                actions.AddRange(created.CreationEffects!.CreationResult.PostActions);
                expectedNames.AddRange(suffixes.Select(suffix => name + suffix));
                if (prefix == "Service")
                {
                    expectedRoutes.Add(name + "-Invoke", name + "/invoke/{appId}/{methodName}");
                }
            }
        }
        ITemplateInfo http = templates.Single(item => item.Identity == "Azure.Function.CSharp.Isolated.HttpTrigger.3.x");
        foreach ((string requested, string actual) in new[]
        {
            ("ServiceFirst_Invoke", "ServiceFirst_Invoke"),
            ("OrchestrateFirst_HttpStart", "OrchestrateFirst_HttpStart"),
            ("PublishFirst_TopicTrigger", "PublishFirst_TopicTrigger"),
            ("Hyphen-Primary", "Hyphen_Primary"),
        })
        {
            ITemplateCreationResult created = await creator.InstantiateAsync(http, requested, requested, output,
                new Dictionary<string, string?> { ["namespace"] = "Sample" }, cancellationToken: timeout.Token);
            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            actions.AddRange(created.CreationEffects!.CreationResult.PostActions);
            expectedNames.Add(actual);
        }
        string customPath = Path.Combine(output, "Custom.cs");
        const string userSource = "namespace Sample; public class Counter { } public class MyConnectionInfo { } "
            + "public class InvokeOutputBinding { } public class DaprTopicTriggerFuncApp { } "
            + "public class NegotiateFirstConnectionInfo { } public class ServiceFirstInvoke { } public class PublishFirstTopicTrigger { }";
        File.WriteAllText(customPath, userSource);
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("The repository NuGet.Config was not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(output, "NuGet.Config"));
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateDependencyExecutor dependencies = new(runner);
        string project = Path.Combine(output, "Sample.csproj");
        foreach (IPostAction action in actions.Where(action => action.ActionId == _packageAction)
            .DistinctBy(action => (action.Args["reference"], action.Args["version"])))
        {
            await dependencies.AddPackageReferenceAsync(project,
                new(action.Args["reference"], action.Args["version"], action.Args["projectFileExtensions"]), timeout.Token);
        }
        await dependencies.RestoreAsync(project, timeout.Token);

        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:minimal"], output, timeout.Token);

        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "bin", "Debug", "net10.0", "functions.metadata")));
        JsonElement[] functions = [.. metadata.RootElement.EnumerateArray()];
        functions.Select(function => function.GetProperty("name").GetString()).Should().BeEquivalentTo(expectedNames)
            .And.OnlyHaveUniqueItems();
        foreach ((string name, string route) in expectedRoutes)
        {
            JsonElement function = functions.Single(function => function.GetProperty("name").GetString() == name);
            JsonElement trigger = function.GetProperty("bindings").EnumerateArray()
                .Single(binding => binding.GetProperty("type").GetString() == "httpTrigger");
            trigger.GetProperty("route").GetString().Should().Be(route);
            JsonElement binding = function.GetProperty("bindings").EnumerateArray()
                .Single(binding => binding.GetProperty("type").GetString() == "daprInvoke");
            binding.GetProperty("appId").GetString().Should().Be("{appId}");
            binding.GetProperty("methodName").GetString().Should().Be("{methodName}");
        }
        File.ReadAllText(customPath).Should().Be(userSource);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeDatabaseItems_DeclaredDependencies_BuildRepeatedFunctionsAndPreserveSampleSqlLogging()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo projectTemplate = templates.Single(template =>
            template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "database-build");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        ITemplateCreationResult projectResult = await creator.InstantiateAsync(projectTemplate, "Sample", "Sample", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: timeout.Token);
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        string project = Path.Combine(output, "Sample.csproj");
        string customPath = Path.Combine(output, "Custom.cs");
        const string customSource = "namespace Sample; public class ToDoItem { } public class SqlOutputFirstItem { }";
        File.WriteAllText(customPath, customSource);
        List<IPostAction> actions = [.. projectResult.CreationEffects!.CreationResult.PostActions];
        List<string> names = [];
        foreach (string provider in new[] { "MySql", "Sql" })
        {
            foreach (string kind in new[] { "Output", "Trigger" })
            {
                ITemplateInfo template = templates.Single(item => item.Identity == $"Azure.Function.CSharp.Isolated.{provider}{kind}Binding");
                foreach (string suffix in new[] { "First", "Second" })
                {
                    string name = provider + kind + suffix;
                    ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output,
                        new Dictionary<string, string?> { ["namespace"] = "Sample" }, cancellationToken: timeout.Token);
                    created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
                    names.Add(name);
                    actions.AddRange(created.CreationEffects!.CreationResult.PostActions);
                }
            }
        }
        File.ReadAllText(customPath).Should().Be(customSource);
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("The repository NuGet.Config was not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(output, "NuGet.Config"));
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateDependencyExecutor dependencies = new(runner);
        foreach (IPostAction action in actions.Where(action => action.ActionId == _packageAction)
            .DistinctBy(action => (action.Args["reference"], action.Args["version"])))
        {
            await dependencies.AddPackageReferenceAsync(project,
                new(action.Args["reference"], action.Args["version"], action.Args["projectFileExtensions"]), timeout.Token);
        }
        await dependencies.RestoreAsync(project, timeout.Token);

        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:minimal"], output, timeout.Token);

        string built = Path.Combine(output, "bin", "Debug", "net10.0");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(built, "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString())
            .Should().BeEquivalentTo(names);
        File.ReadAllText(project).Should().NotContain("Newtonsoft");
        File.ReadAllText(customPath).Should().Be(customSource);
        AssemblyLoadContext load = new("native-sql-sample", isCollectible: true);
        load.Resolving += (context, name) =>
        {
            if (name.Name == typeof(ILogger).Assembly.GetName().Name)
            {
                return typeof(ILogger).Assembly;
            }
            string path = Path.Combine(built, name.Name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }
            using FileStream dependency = File.OpenRead(path);
            return context.LoadFromStream(dependency);
        };
        try
        {
            using FileStream source = File.OpenRead(Path.Combine(built, "Sample.dll"));
            Assembly assembly = load.LoadFromStream(source);
            Type functionType = assembly.GetType("Sample.SqlTriggerFirst", throwOnError: true)!;
            Type dtoType = assembly.GetType("Sample.SqlTriggerFirst+SqlTriggerFirstItem", throwOnError: true)!;
            MethodInfo run = functionType.GetMethod("Run")!;
            Type changeType = run.GetParameters()[0].ParameterType.GetGenericArguments()[0];
            Type operationType = changeType.GetConstructors().Single().GetParameters()[0].ParameterType;
            var changes = Array.CreateInstance(changeType, 3);
            string[] operations = ["Insert", "Update", "Delete"];
            string?[] descriptions = ["<quoted \"value\"> \u96EA", string.Empty, null];
            for (int index = 0; index < changes.Length; index++)
            {
                object row = Activator.CreateInstance(dtoType)!;
                dtoType.GetProperty("Id")!.SetValue(row, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                dtoType.GetProperty("Priority")!.SetValue(row, index - 1);
                dtoType.GetProperty("Description")!.SetValue(row, descriptions[index]);
                changes.SetValue(Activator.CreateInstance(changeType, Enum.Parse(operationType, operations[index]), row), index);
            }
            ILogger logger = Substitute.For<ILogger>();
            ILoggerFactory factory = Substitute.For<ILoggerFactory>();
            factory.CreateLogger(Arg.Any<string>()).Returns(logger);
            object function = Activator.CreateInstance(functionType, factory)!;

            run.Invoke(function, [changes, null]);

            string logged = logger.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "Log")
                .GetArguments()[2]!.ToString()!;
            logged.Should().StartWith("SQL Changes: ");
            string actualJson = logged["SQL Changes: ".Length..];
            string originalJson = Newtonsoft.Json.JsonConvert.SerializeObject(changes);
            JsonNode.DeepEquals(JsonNode.Parse(actualJson), JsonNode.Parse(originalJson)).Should().BeTrue(
                "the sample's row values, property names, numeric operations and nulls must survive the serializer correction");
            using var json = JsonDocument.Parse(actualJson);
            json.RootElement[0].GetProperty("Item").GetProperty("Description").GetString().Should().Be(descriptions[0]);
            json.RootElement[1].GetProperty("Item").GetProperty("Priority").GetInt32().Should().Be(0);
            json.RootElement[2].GetProperty("Item").GetProperty("Description").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            load.Unload();
        }
    }

    [Fact]
    public async Task NativeItems_AllRawConfigurations_RenderSourceAndRetainQualifiedActionShapes()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        ITemplateInfo[] items = [.. templates.Where(template => template.TagsCollection["type"] == "item")];
        items.Should().HaveCount(36);
        TemplateCreator creator = new(session.Settings);
        foreach (ITemplateInfo template in items)
        {
            string output = Path.Combine(_root, template.Identity);
            Dictionary<string, string?> parameters = new() { ["namespace"] = "Qualified.Functions" };
            ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Generated", "Generated", output,
                parameters, dryRun: true, cancellationToken: CancellationToken.None);
            preview.Status.Should().Be(CreationResultStatus.Success, $"{template.Identity}: {preview.ErrorMessage}");
            Directory.Exists(output).Should().BeFalse();
            IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
            actions.Should().NotBeEmpty(template.Identity);
            actions.Should().OnlyContain(action =>
                action.ActionId == _packageAction || action.ActionId == _restoreAction || action.ActionId == _editorAction, template.Identity);
            foreach (IPostAction action in actions.Where(action => action.ActionId == _packageAction))
            {
                action.Args["referenceType"].Should().Be("package");
                DotNetTemplatePackageReference reference = new(action.Args["reference"], action.Args["version"],
                    action.Args["projectFileExtensions"]);
                reference.ProjectFileExtension.Should().Be(template.TagsCollection["language"] == "F#" ? ".fsproj" : ".csproj");
            }

            ITemplateCreationResult created = await creator.InstantiateAsync(template, "Generated", "Generated", output,
                parameters, cancellationToken: CancellationToken.None);
            created.Status.Should().Be(CreationResultStatus.Success, $"{template.Identity}: {created.ErrorMessage}");
            string extension = template.TagsCollection["language"] == "F#" ? "*.fs" : "*.cs";
            Directory.EnumerateFiles(output, extension, SearchOption.AllDirectories).Should().NotBeEmpty(template.Identity);
            created.CreationEffects!.CreationResult.PostActions.Select(action => action.ActionId)
                .Should().Equal(actions.Select(action => action.ActionId), template.Identity);
        }
    }

    [Theory]
    [InlineData("CSharp", ".cs", ".csproj", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "2.1.0")]
    [InlineData("FSharp", ".fs", ".fsproj", "Microsoft.Azure.Functions.Worker.Extensions.Http", "3.3.0")]
    public async Task NativeHttp_ActualPreviewActions_ReachTypedPackageExecutorWithoutDroppingSourceOptions(
        string language, string extension, string projectExtension, string packageId, string version)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        string output = Path.Combine(_root, "http-" + language);
        var parameters = new Dictionary<string, string?> { ["namespace"] = "Sample.Functions", ["AccessRights"] = "Anonymous" };
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Greeting", "Greeting", output, parameters, dryRun: true,
            cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        IReadOnlyList<IPostAction> actions = preview.CreationEffects!.CreationResult.PostActions;
        IPostAction package = actions.Should().ContainSingle(action => action.ActionId == _packageAction).Which;
        package.Args["referenceType"].Should().Be("package");
        package.Args["reference"].Should().Be(packageId);
        package.Args["version"].Should().Be(version);
        package.ContinueOnError.Should().BeTrue();
        actions.Should().Contain(action => action.ActionId == _editorAction && action.ContinueOnError);
        IDotnetCliRunner runner = Substitute.For<IDotnetCliRunner>();
        DotNetTemplateDependencyExecutor executor = new(runner);
        string project = Path.Combine(output, "Sample" + projectExtension);

        await executor.AddPackageReferenceAsync(project,
            new(package.Args["reference"], package.Args["version"], package.Args["projectFileExtensions"]), CancellationToken.None);

        await runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
            new[] { "add", project, "package", packageId, "--version", version })), output, CancellationToken.None);
        ITemplateCreationResult created = await creator.InstantiateAsync(template, "Greeting", "Greeting", output, parameters,
            cancellationToken: CancellationToken.None);
        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "Greeting" + extension)).Should().Contain("Sample.Functions", "AuthorizationLevel.Anonymous");
    }

    private string ExtractInnerArchive()
    {
        string path = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using var outer = new PackageArchiveReader(OuterArchive);
        using FileStream target = File.Create(path);
        using Stream source = outer.GetStream("tools/any/templates/dotnet.nupkg");
        source.CopyTo(target);
        return path;
    }

    private async Task<NativeSession> OpenNativeSessionAsync()
    {
        NativeSession session = new(Path.Combine(_root, "hive"));
        IManagedTemplatePackageProvider provider = session.Manager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(ExtractInnerArchive())], CancellationToken.None);
        results.Should().ContainSingle().Which.Success.Should().BeTrue();
        return session;
    }

    private sealed class NativeSession : IDisposable
    {
        private readonly DefaultTemplateEngineHost _host = new("func", "1.0.0", null,
            [.. Components.AllComponents, .. Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents]);

        public NativeSession(string hive)
        {
            Settings = new(_host, settingsLocation: hive);
            Manager = new(Settings);
        }

        public EngineEnvironmentSettings Settings { get; }

        public TemplatePackageManager Manager { get; }

        public void Dispose()
        {
            Settings.Dispose();
            _host.Dispose();
        }
    }
}
