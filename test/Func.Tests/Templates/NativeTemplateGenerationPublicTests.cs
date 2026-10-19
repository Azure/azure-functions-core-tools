// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private const string NativeFunctionIdentity = "Package.Added.Native.Http";
    private const string NativeProjectIdentity = "Package.Added.Native.NodeProject";

    [Theory]
    [InlineData(null, "NativeHttp", "Hello from a native definition")]
    [InlineData("ExplicitFunction", "ExplicitFunction", "quotes'\" and a newline\nremain values")]
    public async Task NativeDefinition_ActualFunctionsPackage_ProjectPreviewAndItemCreationUseSharedEngine(
        string? name, string expectedName, string greeting)
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive());
        string project = Path.Combine(_root, "native-project");
        string[] init = ["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"];

        var preview = await InvokeNativeDefinitionAsync([.. init, "--dry-run"]);
        preview.Exit.Should().Be(0, preview.Output.AllOutput);
        Directory.Exists(project).Should().BeFalse();
        var created = await InvokeNativeDefinitionAsync(init);
        created.Exit.Should().Be(0, created.Output.AllOutput);
        var configuration = File.ReadAllText(Path.Combine(project, ".func", "config.json"));
        using (var json = JsonDocument.Parse(configuration))
        {
            json.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
            json.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        }
        File.ReadAllText(Path.Combine(project, "package.json")).Should().Contain("@azure/functions", "src/functions/*.js");
        string[] nameTokens = name is null ? [] : ["--name", name];
        string[] item = ["new", project, "-t", NativeFunctionIdentity, "--Greeting", greeting, "--AuthLevel", "function",
            "--non-interactive", "--output", "json", .. nameTokens];

        var itemPreview = await InvokeNativeDefinitionAsync([.. item, "--dry-run"]);
        itemPreview.Exit.Should().Be(0, itemPreview.Output.AllOutput);
        string path = Path.Combine(project, "src", "functions", expectedName + ".js");
        File.Exists(path).Should().BeFalse();
        var itemCreated = await InvokeNativeDefinitionAsync(item);

        itemCreated.Exit.Should().Be(0, itemCreated.Output.AllOutput);
        string content = File.ReadAllText(path);
        content.Should().Contain("require('@azure/functions')", $"app.http('{expectedName}'", "authLevel: 'function'");
        string literal = content[(content.IndexOf("body: ", StringComparison.Ordinal) + "body: ".Length)..].Split(" }; }", StringSplitOptions.None)[0];
        using var body = JsonDocument.Parse(literal);
        body.RootElement.GetString().Should().Be(greeting);
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(configuration);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NativeDefinition_ContextualHelp_UsesAuthoredParametersWithoutLegacySourceQualification()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive());
        string project = Path.Combine(_root, "native-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", NativeFunctionIdentity, "--help"]);

        result.Exit.Should().Be(0, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("--Greeting", "--AuthLevel").And.NotContain("unqualified");
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("quoted'name")]
    [InlineData("not a function name")]
    public async Task NativeDefinition_InvalidFunctionsName_RefusesWithoutTemplateOrConfigurationWrites(string name)
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive());
        string project = Path.Combine(_root, "native-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);
        string configuration = File.ReadAllText(Path.Combine(project, ".func", "config.json"));

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", NativeFunctionIdentity, "-n", name,
            "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("invalidName");
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(configuration);
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    [Fact]
    public async Task NativeDefinition_ArchiveChangesAfterReceipt_DoesNotBorrowOwnedSourceAuthority()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive());
        string archive;
        using (var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            archive = (await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single().MountPointUri;
        }
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry("content/native-http/changed.txt").Open());
            writer.Write("changed after the original ownership receipt");
        }
        string project = Path.Combine(_root, "rejected-project");

        var result = await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        Directory.Exists(project).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDefinition_MigratedHttp_RetainsOriginalValidationAndContentDescriptorProof(bool corruptContent)
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(migrateHttp: true, corruptContent));
        string project = Path.Combine(_root, "migrated-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);
        string configuration = File.ReadAllText(Path.Combine(project, ".func", "config.json"));

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--authlevel=function",
            "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(corruptContent ? 1 : 0, result.Output.AllOutput);
        string generated = Path.Combine(project, "src", "functions", "httpTrigger.js");
        if (corruptContent)
        {
            File.Exists(generated).Should().BeFalse();
            result.Output.AllOutput.Should().Contain("SHA-256 mismatch");
        }
        else
        {
            File.ReadAllText(generated).Should().Contain("app.http('httpTrigger'", "authLevel: 'function'");
            var invalid = await InvokeNativeDefinitionAsync(["new", project, "-t", "HttpTrigger-JavaScript", "-n", "Has-Hyphen",
                "--non-interactive", "--output", "json"]);
            invalid.Exit.Should().Be(1, invalid.Output.AllOutput);
            invalid.Output.AllOutput.Should().Contain("invalidLegacyInput", "original pattern");
            File.Exists(Path.Combine(project, "src", "functions", "Has-Hyphen.js")).Should().BeFalse();
        }
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(configuration);
    }

    [Fact]
    public async Task NativeDefinition_ForceCannotAuthorizeWritingCliConfiguration()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(writesConfiguration: true));
        string project = Path.Combine(_root, "protected-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);
        string configuration = File.ReadAllText(Path.Combine(project, ".func", "config.json"));

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", NativeFunctionIdentity, "--force",
            "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("reserved CLI-owned");
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(configuration);
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    [Fact]
    public async Task NativeDefinition_PostActionCannotAcquireALanguageEffectCapability()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(itemAction: true));
        string project = Path.Combine(_root, "action-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", NativeFunctionIdentity,
            "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("post-actions");
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ControlledComposition_NativeDefinitionsDoNotRequireAStackItemAdapter()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive());
        string project = Path.Combine(_root, "adapter-free-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var listed = await InvokeNativeDefinitionAsync(["new", project, "--list", "--non-interactive", "--output", "json"],
            withoutItemProvider: true);
        var created = await InvokeNativeDefinitionAsync(["new", project, "-t", NativeFunctionIdentity,
            "--non-interactive", "--output", "json"], withoutItemProvider: true);

        listed.Exit.Should().Be(0, listed.Output.AllOutput);
        using var json = JsonDocument.Parse(listed.Output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("templates").EnumerateArray().Select(template => template.GetProperty("identity").GetString())
            .Should().Equal(NativeFunctionIdentity);
        created.Exit.Should().Be(0, created.Output.AllOutput);
        File.ReadAllText(Path.Combine(project, "src", "functions", "NativeHttp.js")).Should().Contain("app.http('NativeHttp'");
    }

    [Fact]
    public async Task NativeDefinition_ReclassifiedDaprCannotReplaceHelperQualificationWithBodyInequality()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(unsafeDapr: true));
        string project = Path.Combine(_root, "dapr-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", "DaprPublishOutputBinding-JavaScript",
            "-n", "DaprTopicTriggerNodeV4", "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("owned source body");
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDefinition_DuplicateProvenanceCannotDiscardAClaimedLegacyValidator(bool migrateHttp)
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(migrateHttp: migrateHttp, duplicateMapping: true));
        string project = Path.Combine(_root, "duplicate-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", "HttpTrigger-JavaScript",
            "-n", "Has-Hyphen", "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain("Duplicate Node provenance");
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    [Fact]
    public async Task ControlledReclassification_QualifiedDaprRetainsItsNarrowHelperPath()
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(migrateDapr: true));
        string project = Path.Combine(_root, "qualified-dapr-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", "DaprPublishOutputBinding-JavaScript",
            "-n", "QualifiedDapr", "--non-interactive", "--output", "json"]);

        result.Exit.Should().Be(0, result.Output.AllOutput);
        File.ReadAllText(Path.Combine(project, "src", "functions", "QualifiedDapr.js"))
            .Should().Contain("app.timer('QualifiedDapr'", "app.generic('QualifiedDapr-Topic'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlledComposition_MissingGenerationAdapterCannotSkipRequiredSourceValidation(bool auxiliary)
    {
        await _installer.InstallFromPackageAsync(NativeDefinitionArchive(migrateHttp: !auxiliary, unsafeDapr: auxiliary));
        string project = Path.Combine(_root, "required-validation-project");
        (await InvokeNativeDefinitionAsync(["init", project, "--template", NativeProjectIdentity, "-s", "node", "-l", "js",
            "--non-interactive", "--output", "json"])).Exit.Should().Be(0);
        string template = auxiliary ? "DaprPublishOutputBinding-JavaScript" : "HttpTrigger-JavaScript";
        string name = auxiliary ? "DaprTopicTriggerNodeV4" : "Has-Hyphen";

        var result = await InvokeNativeDefinitionAsync(["new", project, "-t", template, "-n", name,
            "--non-interactive", "--output", "json"], withoutItemProvider: true);

        result.Exit.Should().Be(1, result.Output.AllOutput);
        result.Output.AllOutput.Should().Contain(auxiliary ? "owned source body" : "original pattern");
        Directory.Exists(Path.Combine(project, "src")).Should().BeFalse();
    }

    private async Task<(int Exit, TestInteractionService Output)> InvokeNativeDefinitionAsync(
        string[] arguments, bool withoutItemProvider = false)
    {
        TestInteractionService output = new();
        int exit = await new NativeCliEntryPoint().ExecuteAsync(arguments, output, _paths, new(Home), CancellationToken.None,
            services =>
            {
                TrackEntryContexts(services);
                if (withoutItemProvider)
                {
                    services.RemoveAll<INativeItemWorkflowProvider>();
                }
            }, _ => Task.FromResult<string?>(null));
        return (exit, output);
    }

    private string NativeDefinitionArchive(bool migrateHttp = false, bool corruptContent = false,
        bool writesConfiguration = false, bool itemAction = false, bool unsafeDapr = false,
        bool duplicateMapping = false, bool migrateDapr = false)
    {
        string path = Path.Combine(_root, "native-definition.nupkg");
        File.Copy(BuiltArchive, path);
        using var outer = ZipFile.Open(path, ZipArchiveMode.Update);
        JsonNode manifest;
        using (var reader = new StreamReader(outer.GetEntry("bundled-templates.json")!.Open()))
        {
            manifest = JsonNode.Parse(reader.ReadToEnd())!;
        }
        const string nestedPath = "tools/any/templates/node-basic.nupkg";
        using var payload = new MemoryStream();
        using (var stream = outer.GetEntry(nestedPath)!.Open())
        {
            stream.CopyTo(payload);
        }
        using (var native = new ZipArchive(payload, ZipArchiveMode.Update, leaveOpen: true))
        {
            Write("content/native-http/.template.config/template.json", """
                {
                  "identity":"Package.Added.Native.Http","name":"Native HTTP function","shortName":"native-http",
                  "tags":{"type":"item","language":"JavaScript","func-generation":"native"},
                  "sourceName":"NativeFunction","defaultName":"NativeHttp","preferNameDirectory":false,
                  "symbols":{
                    "FunctionName":{"type":"derived","valueSource":"name","valueTransform":"identity","replaces":"__FUNCTION_NAME__"},
                    "Greeting":{"type":"parameter","datatype":"string","defaultValue":"Hello from a native definition"},
                    "GreetingLiteral":{"type":"derived","valueSource":"Greeting","valueTransform":"jsonEncode","replaces":"__GREETING_JSON__"},
                    "AuthLevel":{"type":"parameter","datatype":"choice","defaultValue":"anonymous","replaces":"__AUTH_LEVEL__",
                      "choices":[{"choice":"anonymous"},{"choice":"function"}]},
                    "InvocationOutputIntent":{"type":"bind","binding":"host:func:item-output-intent"}
                  },
                  "sources":[
                    {"source":"./","target":"src/functions/","condition":"(InvocationOutputIntent == \"legacy\")"},
                    {"source":"./","target":"./","condition":"(InvocationOutputIntent != \"legacy\")"}
                  ],
                  "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]},"stack":{"type":"func-workload","args":"node"}}
                }
                """);
            Write("content/native-http/NativeFunction.js", """
                const { app } = require('@azure/functions');
                app.http('__FUNCTION_NAME__', {
                    methods: ['GET'], authLevel: '__AUTH_LEVEL__',
                    handler: async () => { return { body: __GREETING_JSON__ }; }
                });
                """);
            Write("content/native-project/.template.config/template.json", """
                {
                  "identity":"Package.Added.Native.NodeProject","name":"Native Functions project","shortName":"native-project",
                  "tags":{"type":"project","language":"JavaScript","func-generation":"native"},
                  "preferNameDirectory":false,
                  "primaryOutputs":[{"path":"host.json"}],
                  "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]},"stack":{"type":"func-workload","args":"node"}},
                  "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56",
                    "args":{"primaryOutputIndex":"0","stack":"node","language":"JavaScript"},"continueOnError":false}]
                }
                """);
            Write("content/native-project/host.json", """{"version":"2.0"}""");
            Write("content/native-project/package.json", """{"name":"native-functions-project","main":"src/functions/*.js","dependencies":{"@azure/functions":"^4.0.0"}}""");
            Write("content/native-project/local.settings.json", """{"IsEncrypted":false,"Values":{"FUNCTIONS_WORKER_RUNTIME":"node","AzureWebJobsStorage":""}}""");

            if (migrateHttp)
            {
                const string configurationPath = "content/http-js/.template.config/template.json";
                var configuration = JsonNode.Parse(ReadEntry(configurationPath))!;
                configuration["tags"]!["func-generation"] = "native";
                string text = configuration.ToJsonString();
                native.GetEntry(configurationPath)!.Delete();
                Write(configurationPath, text);
                const string provenancePath = "provenance/node/compatibility.json";
                var provenance = JsonNode.Parse(ReadEntry(provenancePath))!;
                provenance["templates"]!.AsArray().Single(mapping => mapping!["identity"]!.GetValue<string>()
                    == "Azure.Functions.Node.Http.JavaScript")!["configuration"]!["sha256"]
                    = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
                native.GetEntry(provenancePath)!.Delete();
                string provenanceText = provenance.ToJsonString();
                Write(provenancePath, duplicateMapping ? provenanceText[..^1] + ""","templates":[]}""" : provenanceText);
                if (corruptContent)
                {
                    const string source = "content/http-js/httpTrigger.js";
                    string changed = ReadEntry(source) + "\n// not recorded by the original content descriptor";
                    native.GetEntry(source)!.Delete();
                    Write(source, changed);
                }
            }

            if (duplicateMapping && !migrateHttp)
            {
                const string provenancePath = "provenance/node/compatibility.json";
                string text = ReadEntry(provenancePath);
                native.GetEntry(provenancePath)!.Delete();
                Write(provenancePath, text.TrimEnd()[..^1] + ""","templates":[]}""");
            }

            if (unsafeDapr || migrateDapr)
            {
                const string identity = "DaprPublishOutputBinding-JavaScript";
                string configurationPath = $"content/items/{identity}/.template.config/template.json";
                var configuration = JsonNode.Parse(ReadEntry(configurationPath))!;
                configuration["tags"]!["func-generation"] = "native";
                configuration["constraints"]!.AsObject().Remove("bundle");
                configuration["constraints"]!.AsObject().Remove("bindings");
                string text = configuration.ToJsonString();
                native.GetEntry(configurationPath)!.Delete();
                Write(configurationPath, text);
                var original = JsonNode.Parse(ReadEntry("provenance/node/v2/templates/templates.json"))!.AsArray()
                    .Single(template => template!["id"]!.GetValue<string>() == identity)!;
                const string provenancePath = "provenance/node/compatibility.json";
                var provenance = JsonNode.Parse(ReadEntry(provenancePath))!;
                var mapping = provenance["templates"]!.AsArray().Single(mapping => mapping!["identity"]!.GetValue<string>() == identity)!;
                string source = mapping["content"]!["path"]!.GetValue<string>();
                mapping["configuration"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
                if (unsafeDapr)
                {
                    string body = original["files"]!.AsObject().Single().Value!.GetValue<string>()
                        .Replace("$(FUNCTION_NAME_INPUT)", configuration["sourceName"]!.GetValue<string>(), StringComparison.Ordinal)
                        + "\n// a comment cannot establish safe auxiliary function identities";
                    native.GetEntry(source)!.Delete();
                    Write(source, body);
                    mapping["content"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
                }
                native.GetEntry(provenancePath)!.Delete();
                Write(provenancePath, provenance.ToJsonString());
            }

            if (writesConfiguration)
            {
                const string configurationPath = "content/native-http/.template.config/template.json";
                var configuration = JsonNode.Parse(ReadEntry(configurationPath))!;
                configuration["sources"]![0]!["target"] = "./";
                configuration["sources"]![0]!["modifiers"] = new JsonArray(new JsonObject
                {
                    ["rename"] = new JsonObject { ["NativeFunction.js"] = ".func/config.json" },
                });
                native.GetEntry(configurationPath)!.Delete();
                Write(configurationPath, configuration.ToJsonString());
            }

            if (itemAction)
            {
                const string configurationPath = "content/native-http/.template.config/template.json";
                var configuration = JsonNode.Parse(ReadEntry(configurationPath))!;
                configuration["postActions"] = new JsonArray(new JsonObject
                {
                    ["actionId"] = "210D431B-A78B-4D2F-B762-4ED3E3EA9025",
                    ["args"] = new JsonObject(),
                });
                native.GetEntry(configurationPath)!.Delete();
                Write(configurationPath, configuration.ToJsonString());
            }

            void Write(string name, string content)
            {
                using var writer = new StreamWriter(native.CreateEntry(name).Open());
                writer.Write(content);
            }

            string ReadEntry(string name)
            {
                using var reader = new StreamReader(native.GetEntry(name)!.Open());
                return reader.ReadToEnd();
            }
        }
        byte[] bytes = payload.ToArray();
        outer.GetEntry(nestedPath)!.Delete();
        using (var target = outer.CreateEntry(nestedPath).Open())
        {
            target.Write(bytes);
        }
        var asset = manifest["assets"]![0]!;
        asset["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        asset["templates"]!.AsArray().Add(new JsonObject
        {
            ["path"] = "content/native-http/.template.config/template.json", ["identity"] = NativeFunctionIdentity, ["type"] = "item",
        });
        asset["templates"]!.AsArray().Add(new JsonObject
        {
            ["path"] = "content/native-project/.template.config/template.json", ["identity"] = NativeProjectIdentity, ["type"] = "project",
        });
        outer.GetEntry("bundled-templates.json")!.Delete();
        using var declaration = new StreamWriter(outer.CreateEntry("bundled-templates.json").Open());
        declaration.Write(manifest.ToJsonString());
        return path;
    }
}
