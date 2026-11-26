// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupFeatureCatalogTests
{
    [Theory]
    [InlineData("Contoso.DevKit", "node", "node")]
    [InlineData("Contoso.DevKit", "comet", null)]
    [InlineData("Azure.Functions.Cli.Workloads.Node", null, null)]
    [InlineData("Azure.Functions.Cli.Workloads.Workers.Node", null, null)]
    [InlineData("Azure.Functions.Cli.Workloads.Host.win-x64", null, null)]
    public void InstallHint_UsesDeclaredStackAndRecipe_NotPackageNameOrAliases(string packageId, string? stack, string? expected)
    {
        WorkloadEntry entry = new()
        {
            PackageId = packageId,
            PackageVersion = "1.0.0",
            Aliases = ["node"],
            Stack = stack is null ? null : new(stack, "custom-host", "Custom label"),
        };

        SetupFeatureCatalog.GetFeatureForInstalledPackage(entry).Should().Be(expected);
    }
}
