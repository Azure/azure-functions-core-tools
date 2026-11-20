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
    public const string OtherTypeTemplateIdentity = "Func.Tests.OtherType";
    public const string UntypedTemplateIdentity = "Func.Tests.Untyped";
    public const string ParameterTemplateIdentity = "Func.Tests.Parameters";

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

    /// <summary>
    /// Writes a folder package with one project template whose <c>func.host.json</c> names options for its parameters,
    /// and returns its path.
    /// </summary>
    public static string WriteParameterPackage(string parentDirectory)
    {
        string packageDirectory = Path.Combine(parentDirectory, "parameter-templates");
        WriteTemplate(
            packageDirectory,
            $$"""
            {
              "identity": "{{ParameterTemplateIdentity}}",
              "name": "Func Tests Parameters",
              "shortName": "func-tests-parameters",
              "tags": { "type": "project" },
              "symbols": {
                "Framework": {
                  "type": "parameter",
                  "datatype": "choice",
                  "choices": [ { "choice": "net8.0" }, { "choice": "net10.0" } ],
                  "defaultValue": "net10.0"
                },
                "NoHttp": { "type": "parameter", "datatype": "bool", "defaultValue": "false" }
              }
            }
            """,
            ("host.json", """{ "version": "2.0" }"""),
            ("func.host.json",
            """
            {
              "symbolInfo": {
                "Framework": { "longName": "framework", "shortName": "f" },
                "NoHttp": { "longName": "no-http" }
              }
            }
            """));

        return packageDirectory;
    }

    /// <summary>
    /// Writes a folder package with one project template tagged with <paramref name="language"/> whose <c>Framework</c>
    /// choice parameter is bound to <c>--target-framework</c>/<c>-tfm</c>, and returns its path.
    /// </summary>
    public static string WriteProjectPackage(string parentDirectory, string identity, string language)
    {
        string packageDirectory = Path.Combine(parentDirectory, "project-template");
        WriteTemplate(
            packageDirectory,
            $$"""
            {
              "identity": "{{identity}}",
              "name": "{{identity}}",
              "shortName": "{{identity}}",
              "sourceName": "FuncProject",
              "tags": { "type": "project", "language": "{{language}}" },
              "symbols": {
                "Framework": {
                  "type": "parameter",
                  "datatype": "choice",
                  "choices": [ { "choice": "net10.0" }, { "choice": "net11.0" } ],
                  "defaultValue": "net10.0",
                  "replaces": "TARGET_FRAMEWORK"
                }
              }
            }
            """,
            ("FuncProject.csproj", "<TargetFramework>TARGET_FRAMEWORK</TargetFramework>"),
            ("func.host.json",
            """
            {
              "symbolInfo": {
                "Framework": { "longName": "target-framework", "shortName": "tfm" }
              }
            }
            """));

        return packageDirectory;
    }

    /// <summary>
    /// Creates a templater factory over a unique, empty temp home, for tests that route through the template engine
    /// but don't need any installed templates.
    /// </summary>
    public static TemplaterFactory CreateEmptyTemplaterFactory()
        => new(new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), $"func-tests-templates-{Guid.NewGuid():N}")), []);

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
