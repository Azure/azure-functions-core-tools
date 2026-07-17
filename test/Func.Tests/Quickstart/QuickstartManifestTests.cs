// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

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
        int priority = 100) =>
        new(id, displayName, language, resource, iac,
            "https://github.com/Azure-Samples/test-repo", ".", "refs/tags/v1.0.0",
            shortDescription, longDescription, null, priority);

    [Fact]
    public void Filter_ByLanguage_ReturnsMatchingEntries()
    {
        QuickstartManifest manifest = new(
        [
            CreateEntry(id: "python-entry", language: "Python"),
            CreateEntry(id: "node-entry", language: "TypeScript"),
        ]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "Python");

        results.Should().ContainSingle();
        results[0].Id.Should().Be("python-entry");
    }

    [Fact]
    public void Filter_ByLanguage_IsCaseInsensitive()
    {
        QuickstartManifest manifest = new([CreateEntry(language: "Python")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "python");

        results.Should().ContainSingle();
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

        results.Should().ContainSingle();
        results[0].Id.Should().Be("timer-entry");
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

        results.Should().ContainSingle();
        results[0].Id.Should().Be("bicep-entry");
    }

    [Fact]
    public void Filter_BySearch_MatchesId()
    {
        QuickstartManifest manifest = new([CreateEntry(id: "http-trigger-python-azd")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "python");

        results.Should().ContainSingle();
    }

    [Fact]
    public void Filter_BySearch_MatchesDisplayName()
    {
        QuickstartManifest manifest = new([CreateEntry(displayName: "HTTP Trigger with OpenAI")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "openai");

        results.Should().ContainSingle();
    }

    [Fact]
    public void Filter_BySearch_MatchesLongDescription()
    {
        QuickstartManifest manifest = new([CreateEntry(iac: "none", longDescription: "Deploy with Bicep to Azure")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "Deploy with");

        results.Should().ContainSingle();
    }

    [Fact]
    public void Filter_BySearch_MatchesIac()
    {
        QuickstartManifest manifest = new([CreateEntry(iac: "terraform")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(search: "terraform");

        results.Should().ContainSingle();
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

        results.Should().ContainSingle();
        results[0].Id.Should().Be("match");
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

        results[0].Id.Should().Be("high-priority");
        results[1].Id.Should().Be("mid-priority");
        results[2].Id.Should().Be("low-priority");
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

        results.Count.Should().Be(2);
        results[0].Id.Should().Be("b");
    }

    [Fact]
    public void Filter_NoMatches_ReturnsEmptyList()
    {
        QuickstartManifest manifest = new([CreateEntry(language: "Python")]);

        IReadOnlyList<QuickstartEntry> results = manifest.Filter(language: "Go");

        results.Should().BeEmpty();
    }
}

