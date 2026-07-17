// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/Azure/some-repo")]
    [InlineData("https://github.com/azure/some-repo")]
    [InlineData("https://github.com/Azure-Samples/functions-quickstart-python")]
    [InlineData("https://github.com/azure-samples/functions-quickstart-python")]
    [InlineData("https://github.com/Microsoft/some-repo")]
    [InlineData("https://github.com/microsoft/some-repo")]
    public void IsAllowed_AcceptsTrustedOrganizations(string url)
    {
        QuickstartUrlValidator.IsAllowed(url).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_RejectsNullOrEmpty(string? url)
    {
        QuickstartUrlValidator.IsAllowed(url).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsHttpScheme()
    {
        QuickstartUrlValidator.IsAllowed("http://github.com/Azure/some-repo").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsNonGitHubHost()
    {
        QuickstartUrlValidator.IsAllowed("https://gitlab.com/Azure/some-repo").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsUntrustedOrganization()
    {
        QuickstartUrlValidator.IsAllowed("https://github.com/random-user/some-repo").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsEmbeddedCredentials()
    {
        QuickstartUrlValidator.IsAllowed("https://user:pass@github.com/Azure/some-repo").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsInvalidUri()
    {
        QuickstartUrlValidator.IsAllowed("not-a-url").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsFileScheme()
    {
        QuickstartUrlValidator.IsAllowed("file:///c:/manifests/test.json").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsNonDefaultPort()
    {
        QuickstartUrlValidator.IsAllowed("https://github.com:8080/Azure/some-repo").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_RejectsOrgWithoutRepo()
    {
        QuickstartUrlValidator.IsAllowed("https://github.com/Azure").Should().BeFalse();
    }
}
