// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class QuickstartManifestTests
{
    private static QuickstartEntry Entry(
        string id,
        string language = "CSharp",
        string resource = "HTTP Trigger",
        string? iac = null,
        int priority = 0,
        string displayName = "",
        IReadOnlyList<string>? tags = null,
        string? shortDescription = null) =>
        new()
        {
            Id = id,
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName,
            Language = language,
            Resource = resource,
            Iac = iac,
            RepositoryUrl = "https://github.com/Azure/repo",
            FolderPath = ".",
            Priority = priority,
            Tags = tags ?? [],
            ShortDescription = shortDescription,
        };

    [Fact]
    public void Constructor_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuickstartManifest(null!));
    }

    [Fact]
    public void Filter_NoFilters_ReturnsAllSortedByPriority()
    {
        var manifest = new QuickstartManifest([
            Entry("b", priority: 20),
            Entry("a", priority: 10),
            Entry("c", priority: 30),
        ]);

        var result = manifest.Filter();

        Assert.Equal(["a", "b", "c"], result.Select(e => e.Id));
    }

    [Fact]
    public void Filter_LanguageFilter_CaseInsensitive()
    {
        var manifest = new QuickstartManifest([
            Entry("py", language: "Python"),
            Entry("cs", language: "CSharp"),
        ]);

        var result = manifest.Filter(language: "python");

        Assert.Single(result);
        Assert.Equal("py", result[0].Id);
    }

    [Fact]
    public void Filter_ResourceFilter_CaseInsensitive()
    {
        var manifest = new QuickstartManifest([
            Entry("http", resource: "HTTP Trigger"),
            Entry("timer", resource: "Timer Trigger"),
        ]);

        var result = manifest.Filter(resource: "http trigger");

        Assert.Single(result);
        Assert.Equal("http", result[0].Id);
    }

    [Fact]
    public void Filter_IacFilter_CaseInsensitive()
    {
        var manifest = new QuickstartManifest([
            Entry("bicep", iac: "Bicep"),
            Entry("terra", iac: "Terraform"),
            Entry("none", iac: null),
        ]);

        var result = manifest.Filter(iac: "bicep");

        Assert.Single(result);
        Assert.Equal("bicep", result[0].Id);
    }

    [Fact]
    public void Filter_SearchMatchesId()
    {
        var manifest = new QuickstartManifest([
            Entry("flex-consumption-http"),
            Entry("other"),
        ]);

        var result = manifest.Filter(search: "FLEX");

        Assert.Single(result);
        Assert.Equal("flex-consumption-http", result[0].Id);
    }

    [Fact]
    public void Filter_SearchMatchesDisplayName()
    {
        var manifest = new QuickstartManifest([
            Entry("id1", displayName: "OpenAI Chat Sample"),
            Entry("id2", displayName: "Plain HTTP"),
        ]);

        var result = manifest.Filter(search: "openai");

        Assert.Single(result);
        Assert.Equal("id1", result[0].Id);
    }

    [Fact]
    public void Filter_SearchMatchesResource()
    {
        var manifest = new QuickstartManifest([
            Entry("a", resource: "Timer Trigger"),
            Entry("b", resource: "HTTP Trigger"),
        ]);

        var result = manifest.Filter(search: "timer");

        Assert.Single(result);
        Assert.Equal("a", result[0].Id);
    }

    [Fact]
    public void Filter_SearchMatchesTag()
    {
        var manifest = new QuickstartManifest([
            Entry("a", tags: ["ai", "openai"]),
            Entry("b", tags: ["sql"]),
        ]);

        var result = manifest.Filter(search: "openai");

        Assert.Single(result);
        Assert.Equal("a", result[0].Id);
    }

    [Fact]
    public void Filter_SearchMatchesShortDescription()
    {
        var manifest = new QuickstartManifest([
            Entry("a", shortDescription: "A demo of vector search"),
            Entry("b", shortDescription: "Hello world"),
        ]);

        var result = manifest.Filter(search: "vector");

        Assert.Single(result);
        Assert.Equal("a", result[0].Id);
    }

    [Fact]
    public void Filter_CombinedFilters_AllMustMatch()
    {
        var manifest = new QuickstartManifest([
            Entry("a", language: "Python", resource: "HTTP Trigger"),
            Entry("b", language: "Python", resource: "Timer Trigger"),
            Entry("c", language: "CSharp", resource: "HTTP Trigger"),
        ]);

        var result = manifest.Filter(language: "Python", resource: "HTTP Trigger");

        Assert.Single(result);
        Assert.Equal("a", result[0].Id);
    }

    [Fact]
    public void Filter_NoMatches_ReturnsEmpty()
    {
        var manifest = new QuickstartManifest([Entry("a", language: "Python")]);

        var result = manifest.Filter(language: "Java");

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_NullOrEmptyCriteria_IgnoresFilter(string? value)
    {
        var manifest = new QuickstartManifest([
            Entry("a", language: "Python"),
            Entry("b", language: "CSharp"),
        ]);

        var result = manifest.Filter(language: value);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_PrioritySort_LowerPriorityFirst()
    {
        var manifest = new QuickstartManifest([
            Entry("low", priority: 100),
            Entry("high", priority: 1),
            Entry("mid", priority: 50),
        ]);

        var result = manifest.Filter();

        Assert.Equal(["high", "mid", "low"], result.Select(e => e.Id));
    }
}
