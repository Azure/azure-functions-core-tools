// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
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
    public const string InitProjectTemplateIdentity = "Func.Tests.InitProject";
    public const string InitProjectTemplateShortName = "func-python";
    public const string OtherTypeTemplateIdentity = "Func.Tests.OtherType";
    public const string UntypedTemplateIdentity = "Func.Tests.Untyped";

    public static ITemplaterFactory CreateEmptyTemplaterFactory()
        => CreateTemplaterFactory(Path.Combine(Path.GetTempPath(), "func-init-template-tests", Guid.NewGuid().ToString("N")));

    public static ITemplaterFactory CreateTemplaterFactory(string home)
        => new TemplaterFactory(new CliConfigurationPathsOptions(home), []);

    /// <summary>
    /// Writes a folder package with one item template, one basic project template, and two templates without an item
    /// or project type, and returns its path.
    /// </summary>
    public static string WriteBasicPackage(string parentDirectory, string? itemHostJson = null)
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
            ("ItemFunction.txt", "ItemFunction content"),
            ("func.host.json", itemHostJson ?? """
                {
                  "symbolInfo": {
                    "AuthorizationLevel": { "longName": "auth-level", "shortName": "a" }
                  },
                  "usageExamples": [ "func new --auth-level anonymous" ]
                }
                """),
            ("dotnetcli.host.json", """{"symbolInfo":{"AuthorizationLevel":{"longName":"dotnet-auth"}},"isHidden":true}"""));

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
              "tags": { "type": "project", "language": "python" },
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

    public static string WriteItemPackage(string parentDirectory, string identity, string name)
    {
        string packageDirectory = Path.Combine(parentDirectory, "item-template");
        WriteTemplate(
            packageDirectory,
            $$"""
            {
              "identity": "{{identity}}",
              "name": "{{name}}",
              "shortName": "func-tests-item",
              "tags": { "type": "item" }
            }
            """,
            ("Function.txt", "Function content"));

        return packageDirectory;
    }

    public static string WriteInitProjectPackage(
        string parentDirectory,
        string shortName = InitProjectTemplateShortName,
        string language = "python",
        string alternateShortName = "func-python-alternative",
        string alternateIdentity = $"{InitProjectTemplateIdentity}.Alternative",
        string targetFrameworkAlias = "target-framework",
        string targetFrameworkShortName = "tfm",
        bool alternateFirst = false)
    {
        string packageDirectory = Path.Combine(parentDirectory, "init-templates");
        string templateDirectory = Path.Combine(packageDirectory, alternateFirst ? "z-project" : "project");
        void WriteAlternativeTemplate() => WriteTemplate(
            Path.Combine(packageDirectory, alternateFirst ? "a-project-alternative" : "project-alternative"),
            $$"""
            {
              "identity": "{{alternateIdentity}}",
              "name": "Alternative Python Project",
              "shortName": "{{alternateShortName}}",
              "tags": { "type": "project", "language": "{{language}}" }
            }
            """,
            ("Alternative.txt", "Alternative project"));

        if (alternateFirst)
        {
            WriteAlternativeTemplate();
        }

        WriteTemplate(
            templateDirectory,
            $$"""
            {
              "$schema": "http://json.schemastore.org/template",
              "identity": "{{InitProjectTemplateIdentity}}",
              "name": "Func Tests Init Project",
              "shortName": "{{shortName}}",
              "sourceName": "FunctionsProject",
              "tags": { "type": "project", "language": "{{language}}" },
              "symbols": {
                "TargetFramework": {
                  "type": "parameter",
                  "datatype": "choice",
                  "defaultValue": "net10.0",
                  "replaces": "net10.0",
                  "choices": [ { "choice": "net10.0" }, { "choice": "net11.0" } ]
                },
                "NoHttp": {
                  "type": "parameter",
                  "datatype": "bool",
                  "defaultValue": "false"
                },
                "NoTelemetry": {
                  "type": "parameter",
                  "datatype": "bool",
                  "defaultValue": "false"
                }
              },
              "sources": [
                {
                  "source": "./",
                  "target": "./",
                  "exclude": [ ".template.config/**" ],
                  "modifiers": [ { "condition": "NoHttp", "exclude": [ "Http.txt" ] } ]
                }
              ]
            }
            """,
            ("FunctionsProject.txt", "net10.0"),
            ("func.host.json", $$"""
                {
                  "symbolInfo": {
                    "TargetFramework": { "longName": "{{targetFrameworkAlias}}", "shortName": "{{targetFrameworkShortName}}" },
                    "NoHttp": { "longName": "no-http", "shortName": "x" },
                    "NoTelemetry": { "longName": "no-telemetry", "shortName": "y" },
                    "RemovedParameter": { "longName": "stale" }
                  }
                }
                """));
        File.WriteAllText(Path.Combine(templateDirectory, "Http.txt"), "HTTP function");
        File.WriteAllText(Path.Combine(templateDirectory, "Program.cs"), """
            var builder = FunctionsApplication.CreateBuilder(args);
            #if (!NoTelemetry)
            var telemetry = builder.Services.AddOpenTelemetry();
            #endif
            await builder.Build().RunAsync();
            """);

        if (!alternateFirst)
        {
            WriteAlternativeTemplate();
        }

        return packageDirectory;
    }

    /// <summary>
    /// Installs a folder package into the template engine settings at <paramref name="settingsLocation"/>.
    /// </summary>
    public static async Task InstallAsync(string packageDirectory, string settingsLocation)
    {
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(packageDirectory));
        using EngineEnvironmentSettings settings = new(new FuncTemplateEngineHost(context, []), settingsLocation: settingsLocation);
        using TemplatePackageManager packageManager = new(settings);

        IManagedTemplatePackageProvider provider = packageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(packageDirectory)], CancellationToken.None);

        InstallResult result = results.Should().ContainSingle().Subject;
        result.Success.Should().BeTrue(result.ErrorMessage ?? string.Empty);
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
