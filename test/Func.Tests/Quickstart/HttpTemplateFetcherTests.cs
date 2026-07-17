// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class HttpTemplateFetcherTests
{
    [Theory]
    [InlineData("https://github.com/Azure-Samples/functions-quickstart", "refs/tags/v1.0.0",
        "https://github.com/Azure-Samples/functions-quickstart/archive/refs/tags/v1.0.0.zip")]
    [InlineData("https://github.com/Azure-Samples/functions-quickstart/", "refs/tags/v2.0.0",
        "https://github.com/Azure-Samples/functions-quickstart/archive/refs/tags/v2.0.0.zip")]
    [InlineData("https://github.com/microsoft/my-repo", "refs/tags/release-1.0",
        "https://github.com/microsoft/my-repo/archive/refs/tags/release-1.0.zip")]
    public void BuildArchiveUrl_ValidEntry_ReturnsCorrectUrl(string repoUrl, string gitRef, string expected)
    {
        QuickstartEntry entry = CreateEntry(repositoryUrl: repoUrl, gitRef: gitRef);

        string result = HttpTemplateFetcher.BuildArchiveUrl(entry);

        result.Should().Be(expected);
    }

    [Fact]
    public void BuildArchiveUrl_TrailingSlashOnRepo_StripsSlashBeforeArchive()
    {
        QuickstartEntry entry = CreateEntry(repositoryUrl: "https://github.com/Azure-Samples/repo/");

        string result = HttpTemplateFetcher.BuildArchiveUrl(entry);

        result.Should().NotContain("//archive");
        result.Should().Contain("/archive/refs/tags/v1.0.0.zip");
    }

    [Fact]
    public void BuildArchiveUrl_NonGitHubHost_ThrowsInvalidOperationException()
    {
        QuickstartEntry entry = CreateEntry(repositoryUrl: "https://gitlab.com/org/repo");

        InvalidOperationException ex = FluentActions.Invoking(() => HttpTemplateFetcher.BuildArchiveUrl(entry)).Should().ThrowExactly<InvalidOperationException>().Which;
        ex.Message.Should().Contain("github.com");
    }

    [Fact]
    public void BuildArchiveUrl_GitRefDoesNotGetDoubled()
    {
        QuickstartEntry entry = CreateEntry(gitRef: "refs/tags/v1.0.0");

        string result = HttpTemplateFetcher.BuildArchiveUrl(entry);

        // Must contain exactly one occurrence of "refs/tags/"
        int firstIndex = result.IndexOf("refs/tags/", StringComparison.Ordinal);
        int secondIndex = result.IndexOf("refs/tags/", firstIndex + 1, StringComparison.Ordinal);
        (firstIndex >= 0).Should().BeTrue("URL should contain refs/tags/");
        (secondIndex < 0).Should().BeTrue("URL should not contain refs/tags/ twice");
    }

    private static QuickstartEntry CreateEntry(
        string repositoryUrl = "https://github.com/Azure-Samples/functions-quickstart",
        string gitRef = "refs/tags/v1.0.0")
    {
        return new QuickstartEntry(
            Id: "test-template",
            DisplayName: "Test Template",
            Language: "Python",
            Resource: "http",
            Iac: "bicep",
            RepositoryUrl: repositoryUrl,
            FolderPath: ".",
            GitRef: gitRef,
            ShortDescription: "A test template",
            LongDescription: null,
            WhatsIncluded: null,
            Priority: 1);
    }
}
