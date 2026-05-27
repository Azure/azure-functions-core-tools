// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartManifestTests
{
    private static QuickstartEntry CreateEntry(
        string id = "test-entry",
        string language = "Python",
        string resource = "http",
        string? iac = "bicep",
        string displayName = "Test Entry",
        string? shortDescription = "A test template",
        string? longDescription = null,
        IReadOnlyList<string>? tags = null,
        int priority = 100) =>
        new(id, displayName, language, resource, iac,
            "https://github.com/Azure-Samples/test-repo", ".", "v1.0.0",
            shortDescription, longDescription, null, tags ?? [], priority);

    [Fact]
    public void Filter_ByLanguage_ReturnsMatchingEntries()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "python-entry", language: "Python"),
            CreateEntry(id: "node-entry", language: "TypeScript"),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "Python");

        Assert.Single(results);
        Assert.Equal("python-entry", results[0].Id);
    }

    [Fact]
    public void Filter_ByLanguage_IsCaseInsensitive()
    {
        QuickstartManifest manifest = new([CreateEntry(language: "Python")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "python");

        Assert.Single(results);
    }

    [Fact]
    public void Filter_ByResource_ReturnsMatchingEntries()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "http-entry", resource: "http"),
            CreateEntry(id: "timer-entry", resource: "timer"),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(resource: "timer");

        Assert.Single(results);
        Assert.Equal("timer-entry", results[0].Id);
    }

    [Fact]
    public void Filter_ByIac_ReturnsMatchingEntries()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "bicep-entry", iac: "bicep"),
            CreateEntry(id: "terraform-entry", iac: "terraform"),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(iac: "bicep");

        Assert.Single(results);
        Assert.Equal("bicep-entry", results[0].Id);
    }

    [Fact]
    public void Filter_BySearch_MatchesId()
    {
        QuickstartManifest manifest = new([CreateEntry(id: "http-trigger-python-azd")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "python");

        Assert.Single(results);
    }

    [Fact]
    public void Filter_BySearch_MatchesDisplayName()
    {
        QuickstartManifest manifest = new([CreateEntry(displayName: "HTTP Trigger with OpenAI")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "openai");

        Assert.Single(results);
    }

    [Fact]
    public void Filter_BySearch_MatchesLongDescription()
    {
        QuickstartManifest manifest = new([CreateEntry(iac: "none", longDescription: "Deploy with Bicep to Azure")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "Deploy with");

        Assert.Single(results);
    }

    [Fact]
    public void Filter_BySearch_MatchesIac()
    {
        QuickstartManifest manifest = new([CreateEntry(iac: "terraform")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "terraform");

        Assert.Single(results);
    }

    [Fact]
    public void Filter_MultipleCriteria_AppliesAll()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "match", language: "Python", resource: "http"),
            CreateEntry(id: "wrong-lang", language: "TypeScript", resource: "http"),
            CreateEntry(id: "wrong-resource", language: "Python", resource: "timer"),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "Python", resource: "http");

        Assert.Single(results);
        Assert.Equal("match", results[0].Id);
    }

    [Fact]
    public void Filter_ReturnsResultsOrderedByPriority()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "low-priority", priority: 300),
            CreateEntry(id: "high-priority", priority: 10),
            CreateEntry(id: "mid-priority", priority: 100),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter();

        Assert.Equal("high-priority", results[0].Id);
        Assert.Equal("mid-priority", results[1].Id);
        Assert.Equal("low-priority", results[2].Id);
    }

    [Fact]
    public void Filter_NoCriteria_ReturnsAllEntriesOrdered()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "a", priority: 2),
            CreateEntry(id: "b", priority: 1),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter();

        Assert.Equal(2, results.Count);
        Assert.Equal("b", results[0].Id);
    }

    [Fact]
    public void Filter_NoMatches_ReturnsEmptyList()
    {
        QuickstartManifest manifest = new([CreateEntry(language: "Python")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "Go");

        Assert.Empty(results);
    }
}
