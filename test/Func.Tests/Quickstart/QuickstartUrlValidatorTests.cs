// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

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
        Assert.True(QuickstartUrlValidator.IsAllowed(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_RejectsNullOrEmpty(string? url)
    {
        Assert.False(QuickstartUrlValidator.IsAllowed(url));
    }

    [Fact]
    public void IsAllowed_RejectsHttpScheme()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("http://github.com/Azure/some-repo"));
    }

    [Fact]
    public void IsAllowed_RejectsNonGitHubHost()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://gitlab.com/Azure/some-repo"));
    }

    [Fact]
    public void IsAllowed_RejectsUntrustedOrganization()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://github.com/random-user/some-repo"));
    }

    [Fact]
    public void IsAllowed_RejectsEmbeddedCredentials()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://user:pass@github.com/Azure/some-repo"));
    }

    [Fact]
    public void IsAllowed_RejectsInvalidUri()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("not-a-url"));
    }

    [Fact]
    public void IsAllowed_RejectsFileScheme()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("file:///c:/manifests/test.json"));
    }

    [Fact]
    public void IsAllowed_RejectsNonDefaultPort()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://github.com:8080/Azure/some-repo"));
    }

    [Fact]
    public void IsAllowed_RejectsOrgWithoutRepo()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://github.com/Azure"));
    }
}
