// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    public const string MetadataTemplateIdentity = "Func.Tests.Metadata";
    public const string InvalidHostTemplateIdentity = "Func.Tests.InvalidHost";
    public const string DotnetHostOnlyTemplateIdentity = "Func.Tests.DotnetHostOnly";

    /// <summary>
    /// Writes a folder package with one item template and one basic project template, and returns its path.
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

        return packageDirectory;
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
