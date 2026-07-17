// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupFeatureCatalogTests
{
    [Theory]
    [InlineData("Azure.Functions.Cli.Workloads.Host.win-x64", SetupFeatureCatalog.HostFeature)]
    [InlineData("azure.functions.cli.workloads.host.linux-x64", SetupFeatureCatalog.HostFeature)]
    [InlineData("Azure.Functions.Cli.Workloads.Workers.Python.win-x64", "python")]
    [InlineData("azure.functions.cli.workloads.workers.python.osx-arm64", "python")]
    [InlineData("Azure.Functions.Cli.Workloads.Workers.Node", "node")]
    [InlineData("Azure.Functions.Cli.Workloads.Templates.Python", "python")]
    [InlineData("Azure.Functions.Cli.Workloads.Python", "python")]
    public void TryGetFeatureForPackageId_ReturnsExpectedFeature(string packageId, string expectedFeature)
    {
        bool matched = SetupFeatureCatalog.TryGetFeatureForPackageId(packageId, out string feature);

        matched.Should().BeTrue();
        feature.Should().Be(expectedFeature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomeOther.Package")]
    public void TryGetFeatureForPackageId_ReturnsFalse_ForUnknownIds(string? packageId)
    {
        bool matched = SetupFeatureCatalog.TryGetFeatureForPackageId(packageId, out string feature);

        matched.Should().BeFalse();
        feature.Should().Be(string.Empty);
    }
}
