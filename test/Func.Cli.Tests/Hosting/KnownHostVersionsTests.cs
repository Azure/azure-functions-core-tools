// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class KnownHostVersionsTests
{
    [Fact]
    public void RecommendedVersion_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(KnownHostVersions.RecommendedVersion));
    }

    [Fact]
    public void HostPackageId_IsCorrect()
    {
        Assert.Equal("Microsoft.Azure.WebJobs.Script.WebHost", KnownHostVersions.HostPackageId);
    }

    [Fact]
    public void IsVerified_RecommendedVersion_ReturnsTrue()
    {
        Assert.True(KnownHostVersions.IsVerified(KnownHostVersions.RecommendedVersion));
    }

    [Fact]
    public void IsVerified_UnknownVersion_ReturnsFalse()
    {
        Assert.False(KnownHostVersions.IsVerified("0.0.0-nonexistent"));
    }

    [Fact]
    public void IsVerified_CaseInsensitive()
    {
        var version = KnownHostVersions.RecommendedVersion.ToUpperInvariant();
        Assert.True(KnownHostVersions.IsVerified(version));
    }
}
