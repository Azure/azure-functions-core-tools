// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class QuickstartUrlValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_NullOrWhitespace_ReturnsFalse(string? url)
    {
        Assert.False(QuickstartUrlValidator.IsAllowed(url));
    }

    [Fact]
    public void IsAllowed_HttpScheme_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("http://github.com/azure/my-repo"));
    }

    [Fact]
    public void IsAllowed_FtpScheme_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("ftp://github.com/azure/my-repo"));
    }

    [Fact]
    public void IsAllowed_NotGithubCom_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://gitlab.com/azure/my-repo"));
    }

    [Fact]
    public void IsAllowed_GithubComSubdomain_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://evil.github.com/azure/my-repo"));
    }

    [Fact]
    public void IsAllowed_UrlWithUserInfo_ReturnsFalse()
    {
        // Embedded credentials are always rejected, even on trusted orgs.
        Assert.False(QuickstartUrlValidator.IsAllowed("https://attacker@github.com/azure/my-repo"));
    }

    [Fact]
    public void IsAllowed_UrlWithUserAndPassword_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://user:pass@github.com/azure/my-repo"));
    }

    [Theory]
    [InlineData("https://github.com/azure/my-repo")]
    [InlineData("https://github.com/Azure/my-repo")]
    [InlineData("https://github.com/AZURE/my-repo")]
    public void IsAllowed_AzureOrg_ReturnsTrue(string url)
    {
        Assert.True(QuickstartUrlValidator.IsAllowed(url));
    }

    [Theory]
    [InlineData("https://github.com/azure-samples/my-repo")]
    [InlineData("https://github.com/Azure-Samples/my-repo")]
    public void IsAllowed_AzureSamplesOrg_ReturnsTrue(string url)
    {
        Assert.True(QuickstartUrlValidator.IsAllowed(url));
    }

    [Fact]
    public void IsAllowed_MicrosoftOrg_ReturnsTrue()
    {
        Assert.True(QuickstartUrlValidator.IsAllowed("https://github.com/microsoft/my-repo"));
    }

    [Fact]
    public void IsAllowed_UntrustedOrg_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("https://github.com/attacker/my-repo"));
    }

    [Fact]
    public void IsAllowed_TrustedOrgWithSubPath_ReturnsTrue()
    {
        Assert.True(QuickstartUrlValidator.IsAllowed("https://github.com/Azure/azure-functions-templates/tree/main/folder"));
    }

    [Fact]
    public void IsAllowed_NotAUri_ReturnsFalse()
    {
        Assert.False(QuickstartUrlValidator.IsAllowed("not a url at all"));
    }

    [Theory]
    [InlineData("azure")]
    [InlineData("Azure")]
    [InlineData("azure-samples")]
    [InlineData("microsoft")]
    public void TrustedOrganizations_ContainsExpectedOrgs(string org)
    {
        Assert.Contains(org, QuickstartUrlValidator.TrustedOrganizations);
    }
}
