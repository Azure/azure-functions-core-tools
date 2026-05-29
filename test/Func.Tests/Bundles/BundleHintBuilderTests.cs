// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Bundles.Tests;

public class BundleHintBuilderTests
{
    [Fact]
    public void EmptyIntersection_MentionsBothRanges()
    {
        string hint = BundleHintBuilder.EmptyIntersection(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[3.*, 4.0.0)",
            "[4.*, 5.0.0)",
            highestVersionSatisfyingHostJsonOnly: "3.36.0",
            profileName: "stable");

        Assert.Contains("[3.*, 4.0.0)", hint);
        Assert.Contains("[4.*, 5.0.0)", hint);
        Assert.Contains("stable", hint);
        Assert.Contains("3.36.0", hint);
        Assert.Contains("Widen", hint);
    }

    [Fact]
    public void NoCompatibleInstall_ListsInstalledAndInstallCommand()
    {
        string hint = BundleHintBuilder.NoCompatibleInstall(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[4.22.0, 5.0.0)",
            ["4.10.0", "4.20.0"],
            suggestedVersion: "4.10.0");

        Assert.Contains("4.10.0", hint);
        Assert.Contains("4.20.0", hint);
        Assert.Contains("[4.22.0, 5.0.0)", hint);
        Assert.Contains($"func workload install {IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.10.0 --force", hint);
    }

    [Fact]
    public void NoCompatibleInstall_NoInstalled_SaysSo()
    {
        string hint = BundleHintBuilder.NoCompatibleInstall(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[4.22.0, 5.0.0)",
            [],
            suggestedVersion: null);

        Assert.Contains("No bundle workload is installed.", hint);
        Assert.Contains("@<version>", hint);
    }

    [Fact]
    public void WorkloadMissing_MentionsInstallCommand()
    {
        string hint = BundleHintBuilder.WorkloadMissing();

        Assert.Contains($"func workload install {IInstalledBundleWorkloads.BundleWorkloadPackageId}", hint);
        Assert.DoesNotContain("func setup", hint);
    }

    [Fact]
    public void WorkloadMissing_KnownRuntime_AppendsSetupFeatureHint()
    {
        string hint = BundleHintBuilder.WorkloadMissing(workerRuntime: "go");

        Assert.Contains("func workload install", hint);
        Assert.Contains("func setup --features go", hint);
    }

    [Fact]
    public void WorkloadMissing_UnknownRuntime_DoesNotAppendSetupHint()
    {
        string hint = BundleHintBuilder.WorkloadMissing(workerRuntime: "ruby");

        Assert.Contains("func workload install", hint);
        Assert.DoesNotContain("func setup", hint);
    }

    [Fact]
    public void NoCompatibleInstall_KnownRuntime_AppendsSetupFeatureHint()
    {
        string hint = BundleHintBuilder.NoCompatibleInstall(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[4.22.0, 5.0.0)",
            ["4.10.0"],
            suggestedVersion: "4.10.0",
            workerRuntime: "node");

        Assert.Contains("func setup --features node", hint);
    }

    [Fact]
    public void EmptyIntersection_DoesNotIncludeSetupHint()
    {
        // Empty-intersection is a host.json/profile mismatch, not a missing install,
        // so `func setup` is not the right remediation and should not appear.
        string hint = BundleHintBuilder.EmptyIntersection(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[3.*, 4.0.0)",
            "[4.*, 5.0.0)",
            highestVersionSatisfyingHostJsonOnly: "3.36.0",
            profileName: "stable");

        Assert.DoesNotContain("func setup", hint);
    }
}
