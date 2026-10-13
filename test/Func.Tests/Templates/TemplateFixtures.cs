// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Tests.Templates;

/// <summary>
/// Template packages shared by template engine and command tests.
/// </summary>
internal static class TemplateFixtures
{
    public const string ItemTemplateIdentity = "Func.Tests.Item";
    public const string ItemTemplateShortName = "func-tests-item";
    public const string ProjectTemplateIdentity = "Func.Tests.Project";
    public const string ProjectTemplateShortName = "func-tests-project";
    public const string OtherTypeTemplateIdentity = "Func.Tests.OtherType";
    public const string UntypedTemplateIdentity = "Func.Tests.Untyped";
    public const string MetadataTemplateIdentity = "Func.Tests.Metadata";
    public const string InvalidHostTemplateIdentity = "Func.Tests.InvalidHost";
    public const string DotnetHostOnlyTemplateIdentity = "Func.Tests.DotnetHostOnly";
    public const string HiddenTemplateIdentity = "Func.Tests.Hidden";

    /// <summary>
    /// Writes a folder package with one item template, one basic project template, and two templates without an item
    /// or project type, and returns its path.
    /// </summary>
    public static string WriteBasicPackage(string parentDirectory)
    {
        string packageDirectory = Path.Combine(parentDirectory, "basic-templates");

        WriteTemplate(
            Path.Combine(packageDirectory, "item"),
            $$"""
            {
              "$schema": "http://json.schemastore.org/template",
              "author": "func-tests",
              "classifications": [ "Test" ],
              "identity": "{{ItemTemplateIdentity}}",
              "name": "Func Tests Item",
              "shortName": "{{ItemTemplateShortName}}",
              "tags": { "type": "item" },
              "sourceName": "ItemFunction"
            }
            """,
            ("ItemFunction.txt", "ItemFunction content"));

        WriteTemplate(
            Path.Combine(packageDirectory, "project"),
            $$"""
            {
              "$schema": "http://json.schemastore.org/template",
              "author": "func-tests",
              "classifications": [ "Test" ],
              "identity": "{{ProjectTemplateIdentity}}",
              "name": "Func Tests Project",
              "shortName": "{{ProjectTemplateShortName}}",
              "tags": { "type": "project" },
              "sourceName": "FuncProject"
            }
            """,
            ("host.json", """{ "version": "2.0" }"""));

        WriteTemplate(
            Path.Combine(packageDirectory, "other-type"),
            $$"""
            {
              "identity": "{{OtherTypeTemplateIdentity}}",
              "name": "Func Tests Other Type",
              "shortName": "func-tests-other-type",
              "tags": { "type": "solution" }
            }
            """,
            ("OtherType.txt", "OtherType content"));

        WriteTemplate(
            Path.Combine(packageDirectory, "untyped"),
            $$"""
            {
              "identity": "{{UntypedTemplateIdentity}}",
              "name": "Func Tests Untyped",
              "shortName": "func-tests-untyped"
            }
            """,
            ("Untyped.txt", "Untyped content"));

        return packageDirectory;
    }

    public static string WriteConstraintPackage(string parentDirectory, string constraints)
    {
      string packageDirectory = Path.Combine(parentDirectory, "constraint-templates");
      WriteTemplate(
        Path.Combine(packageDirectory, "item"),
        $$"""
        {
          "identity": "Func.Tests.ConstrainedItem",
          "name": "Func Tests Constrained Item",
          "shortName": "func-tests-constrained-item",
          "tags": { "type": "item" },
          "sourceName": "ItemFunction",
          "constraints": {{constraints}}
        }
        """,
        ("ItemFunction.txt", "ItemFunction content"));
      return packageDirectory;
    }

    /// <summary>
    /// Writes a folder package whose templates exercise func host metadata, and returns its path.
    /// </summary>
    /// <remarks>
    /// The metadata template also declares <c>name</c>, tags, a bind symbol and a parameter without a datatype.
    /// </remarks>
    public static string WriteMetadataPackage(string parentDirectory)
    {
        string packageDirectory = Path.Combine(parentDirectory, "metadata-templates");

        WriteTemplate(
            Path.Combine(packageDirectory, "metadata"),
            $$"""
            {
              "$schema": "http://json.schemastore.org/template",
              "author": "func-tests",
              "identity": "{{MetadataTemplateIdentity}}",
              "name": "Func Tests Metadata",
              "shortName": "func-tests-metadata",
              "tags": { "type": "item", "language": "TypeScript" },
              "sourceName": "MetadataFunction",
              "symbols": {
                "name": { "type": "parameter", "datatype": "string", "isRequired": true },
                "Level": {
                  "type": "parameter",
                  "datatype": "choice",
                  "defaultValue": "Low",
                  "choices": [ { "choice": "Low", "description": "Low level" }, { "choice": "High" } ]
                },
                "Namespace": { "type": "parameter", "defaultValue": "Company.Function" },
                "FuncStack": { "type": "bind", "binding": "host:func:stack" }
              }
            }
            """,
            ("MetadataFunction.txt", "MetadataFunction content"),
            ("func.host.json", """{ "symbolInfo": { "Level": { "longName": "log-level", "shortName": "l" } } }"""),
            ("dotnetcli.host.json", """{ "symbolInfo": { "Level": { "longName": "dotnet-level" } } }"""));

        WriteTemplate(
            Path.Combine(packageDirectory, "invalid-host"),
            $$"""
            {
              "identity": "{{InvalidHostTemplateIdentity}}",
              "name": "Func Tests Invalid Host",
              "shortName": "func-tests-invalid-host",
              "tags": { "type": "item" }
            }
            """,
            ("InvalidHost.txt", "InvalidHost content"),
            ("func.host.json", "{ not json"));

        WriteTemplate(
            Path.Combine(packageDirectory, "dotnet-host-only"),
            $$"""
            {
              "identity": "{{DotnetHostOnlyTemplateIdentity}}",
              "name": "Func Tests Dotnet Host Only",
              "shortName": "func-tests-dotnet-host-only",
              "tags": { "type": "item" }
            }
            """,
            ("DotnetHostOnly.txt", "DotnetHostOnly content"),
            ("dotnetcli.host.json", """{ "isHidden": true }"""));

        WriteTemplate(
            Path.Combine(packageDirectory, "hidden"),
            $$"""
            {
              "identity": "{{HiddenTemplateIdentity}}",
              "name": "Func Tests Hidden",
              "shortName": "func-tests-hidden",
              "tags": { "type": "item" }
            }
            """,
            ("Hidden.txt", "Hidden content"),
            ("func.host.json", """{ "isHidden": true }"""));

        return packageDirectory;
    }

    public static string WriteJourneyPackage(string parentDirectory)
    {
        string packageDirectory = Path.Combine(parentDirectory, "journey-templates");
        WriteTemplate(
            Path.Combine(packageDirectory, "item"),
            """
            {
              "identity": "Func.Tests.Journey",
              "name": "Func Tests Journey",
              "shortName": "func-tests-journey",
              "tags": { "type": "item", "language": "CSharp" },
              "sourceName": "ItemFunction",
              "constraints": { "stack": { "type": "func-workload", "args": "node" } },
              "symbols": {
                "Greeting": { "type": "parameter", "datatype": "string", "defaultValue": "hello", "replaces": "GREETING" }
              }
            }
            """,
            ("ItemFunction.txt", "ItemFunction GREETING"));
        return packageDirectory;
    }

      public static string WriteProjectJourneyPackage(string parentDirectory)
      {
        string packageDirectory = Path.Combine(parentDirectory, "project-journey-templates");
        WriteTemplate(Path.Combine(packageDirectory, "project"),
          """
          {
            "identity":"Func.Tests.Project","name":"Project journey","shortName":"func-tests-project",
            "tags":{"type":"project"},"sourceName":"FuncProject",
            "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
            "primaryOutputs":[{"path":"host.json"}],
            "symbols":{"Greeting":{"type":"parameter","datatype":"string","defaultValue":"hello","replaces":"GREETING"}},
            "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56",
            "args":{"primaryOutputIndex":"0","stack":"python","language":"py"}}]
          }
          """,
            ("host.json", "{\"version\":\"2.0\"}"));
          File.WriteAllText(Path.Combine(packageDirectory, "project", "FuncProject.txt"), "FuncProject GREETING");
        return packageDirectory;
      }

    public static string WriteRunnableNodeProjectPackage(string parentDirectory)
    {
      string package = WriteProjectJourneyPackage(parentDirectory);
      string project = Path.Combine(package, "project");
      string configurationPath = Path.Combine(project, ".template.config", "template.json");
      JsonNode configuration = JsonNode.Parse(File.ReadAllText(configurationPath))!;
      configuration["identity"] = "Func.Tests.RunnableNodeProject";
      configuration["shortName"] = "func-tests-runnable-node";
      configuration["tags"]!["language"] = "JavaScript";
      configuration["postActions"]![0]!["args"]!["stack"] = "node";
      configuration["postActions"]![0]!["args"]!["language"] = "js";
      File.WriteAllText(configurationPath, configuration.ToJsonString());
      File.Delete(Path.Combine(project, "FuncProject.txt"));
      File.WriteAllText(Path.Combine(project, "package.json"), """{"name":"funcproject","version":"1.0.0","private":true}""");
      File.WriteAllText(Path.Combine(project, "local.settings.json"),
        """{"IsEncrypted":false,"Values":{"AzureWebJobsStorage":"UseDevelopmentStorage=true","FUNCTIONS_WORKER_RUNTIME":"node"}}""");
      string function = Path.Combine(project, "Hello");
      Directory.CreateDirectory(function);
      File.WriteAllText(Path.Combine(function, "function.json"),
        """
        {
          "bindings":[
          {"authLevel":"anonymous","type":"httpTrigger","direction":"in","name":"req","methods":["get"],"route":"hello"},
          {"type":"http","direction":"out","name":"res"}
          ],
          "scriptFile":"index.js"
        }
        """);
      File.WriteAllText(Path.Combine(function, "index.js"),
        """
        module.exports = async function (context) {
          context.res = { status: 200, body: "GREETING from FuncProject" };
        };
        """);
      return package;
    }

    public static string WriteSharedJourneyPackage(string parentDirectory, bool shared = true, string? packageTypeVersion = null, bool multiple = false)
    {
        string folder = WriteJourneyPackage(parentDirectory);
        if (multiple)
        {
            WriteTemplate(
                Path.Combine(folder, "second-item"),
                """{"identity":"Func.Tests.SecondItem","name":"Second item","shortName":"func-tests-second-item","tags":{"type":"item"},"sourceName":"SecondFunction"}""",
                ("SecondFunction.txt", "SecondFunction content"));
        }

        string nuspec = """
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Func.Tests.Shared</id>
                <version>1.0.0</version>
                <authors>func-tests</authors>
                <description>Shared host fixture.</description>
                <packageTypes>
                  <packageType name="Template" />
                  <packageType name="FuncTemplate" />
                </packageTypes>
              </metadata>
            </package>
            """;
        var metadata = XDocument.Parse(nuspec);
        XElement types = metadata.Root!.Element("metadata")!.Element("packageTypes")!;
        if (!shared)
        {
            types.Elements("packageType").Single(type => type.Attribute("name")!.Value == "Template").Remove();
        }

        if (packageTypeVersion is not null)
        {
            types.Elements("packageType").First().SetAttributeValue("version", packageTypeVersion);
        }

        File.WriteAllText(Path.Combine(folder, "Func.Tests.Shared.nuspec"), metadata.ToString());
        string package = Path.Combine(parentDirectory, "Func.Tests.Shared.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(folder, package);
        return package;
    }

    /// <summary>
    /// Installs a folder package into the template engine settings at <paramref name="settingsLocation"/>.
    /// </summary>
    public static async Task InstallAsync(string packageDirectory, string settingsLocation)
    {
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(packageDirectory));
        using TemplateEngineSession session = new(context, settingsLocation);

        IManagedTemplatePackageProvider provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(packageDirectory)], CancellationToken.None);

        InstallResult result = results.Should().ContainSingle().Subject;
        result.Success.Should().BeTrue(result.ErrorMessage ?? string.Empty);
    }

    /// <summary>
    /// Loads the installed template with <paramref name="identity"/> through a new session on <paramref name="settingsLocation"/>.
    /// </summary>
    /// <remarks>
    /// The first load after an install comes from a fresh scan and later loads come from the engine's saved cache.
    /// </remarks>
    public static async Task<ITemplateInfo> GetInstalledTemplateAsync(string settingsLocation, string identity)
    {
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(settingsLocation));
        using TemplateEngineSession session = new(context, settingsLocation);

        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        return templates.Should().ContainSingle(template => template.Identity == identity).Subject;
    }

    private static void WriteTemplate(
        string templateDirectory,
        string templateJson,
        (string Name, string Content) contentFile,
        params (string Name, string Content)[] hostFiles)
    {
        string configDirectory = Path.Combine(templateDirectory, ".template.config");
        Directory.CreateDirectory(configDirectory);

        File.WriteAllText(Path.Combine(configDirectory, "template.json"), templateJson);
        foreach ((string name, string content) in hostFiles)
        {
            File.WriteAllText(Path.Combine(configDirectory, name), content);
        }

        File.WriteAllText(Path.Combine(templateDirectory, contentFile.Name), contentFile.Content);
    }
}
