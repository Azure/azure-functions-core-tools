// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class BundledTemplateVersionsTests
{
    [Fact]
    public void ItemTemplatesVersion_IsNotEmpty()
    {
        var version = BundledTemplateVersions.ItemTemplatesVersion;
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void ProjectTemplatesVersion_IsNotEmpty()
    {
        var version = BundledTemplateVersions.ProjectTemplatesVersion;
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void Versions_AreValidSemverLike()
    {
        // Bundled versions should contain at least one dot (e.g., "4.0.5337")
        Assert.Contains(".", BundledTemplateVersions.ItemTemplatesVersion);
        Assert.Contains(".", BundledTemplateVersions.ProjectTemplatesVersion);
    }
}
