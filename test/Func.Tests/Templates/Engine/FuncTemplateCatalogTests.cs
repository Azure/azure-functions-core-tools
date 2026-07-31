// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Integration coverage for <see cref="FuncTemplateCatalog"/> over the real
/// Node/Python template packages: stack/language filtering, group-identity
/// dedupe, func.host.json presentation hints, constraint exclusion, and
/// per-template scan isolation.
/// </summary>
public sealed class FuncTemplateCatalogTests
{
    private static TemplateListContext ListContext(EngineIntegrationHarness harness, string stack, string? language)
    {
        string directory = harness.NewProjectDirectory();
        return new TemplateListContext(new WorkingDirectory(new DirectoryInfo(directory), true, directory), stack, language);
    }

    [Fact]
    public async Task ListAsync_NodeStack_ReturnsOnlyItemTemplatesForStack()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "node", null), CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Stack == "node");
        results.Should().Contain(r => r.Id == "http");
    }

    [Fact]
    public async Task ListAsync_NoLanguageFilter_DedupesLanguageVariantsByGroup()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "node", null), CancellationToken.None);

        results.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        FunctionTemplateInfo http = results.Single(r => r.Id == "http");
        http.Languages.Should().Contain("javascript").And.Contain("typescript");
    }

    [Fact]
    public async Task ListAsync_LanguageFilter_ReturnsOnlyRequestedLanguage()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "node", "typescript"), CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Languages.Count == 1 && r.Languages[0] == "typescript");
    }

    [Fact]
    public async Task ListAsync_MalformedTemplate_IsSkippedWithWarningAndRestSurvive()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);

        IReadOnlyList<FunctionTemplateInfo> baseline =
            await harness.CreateCatalog().ListAsync(ListContext(harness, "node", null), CancellationToken.None);
        baseline.Count.Should().BeGreaterThanOrEqualTo(2);

        var logger = new CapturingLogger<FuncTemplateCatalog>();
        var throwingReader = new ThrowOnceMountFileReader(new EngineTemplateMountFileReader(harness.Session));
        FuncTemplateCatalog catalog = harness.CreateCatalog(throwingReader, logger);

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "node", null), CancellationToken.None);

        results.Should().HaveCount(baseline.Count - 1);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains('['));
    }

    [Fact]
    public async Task ListAsync_FuncHostJson_AppliesAliasHidesSymbolAndSurfacesValidator()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.PythonPackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "python", null), CancellationToken.None);

        FunctionTemplateInfo http = results.Single(r => r.Id == "http");
        IReadOnlyList<TemplateUserPrompt> prompts = http.Metadata.UserPrompts;

        prompts.Single(p => p.Id == "AuthLevel").LongAlias.Should().Be("--auth-level");
        prompts.Should().NotContain(p => p.Id == "AppObject");
        prompts.Single(p => p.Id == "name").ValidatorRegex.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListAsync_RestrictedTemplate_ExcludedWhenBundleMissing()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        IReadOnlyList<FunctionTemplateInfo> results =
            await catalog.ListAsync(ListContext(harness, "node", null), CancellationToken.None);

        results.Should().NotContain(r => r.Id == "http");
    }

    [Fact]
    public async Task FindRestrictedAsync_RestrictedTemplate_ReturnsReasonWithCallToAction()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        RestrictedTemplateInfo? restricted =
            await catalog.FindRestrictedAsync(ListContext(harness, "node", null), "http", CancellationToken.None);

        restricted.Should().NotBeNull();
        restricted!.Template.Id.Should().Be("http");
        restricted.Reason.Should().Contain("extension bundle").And.Contain("host.json");
    }

    [Fact]
    public async Task FindRestrictedAsync_AllowedOrUnknownTemplate_ReturnsNull()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateCatalog catalog = harness.CreateCatalog();

        RestrictedTemplateInfo? whenAllowed =
            await catalog.FindRestrictedAsync(ListContext(harness, "node", null), "http", CancellationToken.None);
        RestrictedTemplateInfo? whenUnknown =
            await catalog.FindRestrictedAsync(ListContext(harness, "node", null), "does-not-exist", CancellationToken.None);

        whenAllowed.Should().BeNull();
        whenUnknown.Should().BeNull();
    }
}
