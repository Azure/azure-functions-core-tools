// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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

        hint.Should().Contain("[3.*, 4.0.0)");
        hint.Should().Contain("[4.*, 5.0.0)");
        hint.Should().Contain("stable");
        hint.Should().Contain("3.36.0");
        hint.Should().Contain("Widen");
    }

    [Fact]
    public void NoCompatibleInstall_ListsInstalledAndInstallCommand()
    {
        string hint = BundleHintBuilder.NoCompatibleInstall(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[4.22.0, 5.0.0)",
            ["4.10.0", "4.20.0"],
            suggestedVersion: "4.10.0");

        hint.Should().Contain("4.10.0");
        hint.Should().Contain("4.20.0");
        hint.Should().Contain("[4.22.0, 5.0.0)");
        hint.Should().Contain($"func workload install {IInstalledBundleWorkloads.BundleWorkloadPackageId}@4.10.0 --force");
    }

    [Fact]
    public void NoCompatibleInstall_NoInstalled_SaysSo()
    {
        string hint = BundleHintBuilder.NoCompatibleInstall(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "[4.22.0, 5.0.0)",
            [],
            suggestedVersion: null);

        hint.Should().Contain("No bundle workload is installed.");
        hint.Should().Contain("@<version>");
    }

    [Fact]
    public void WorkloadMissing_MentionsInstallCommand()
    {
        string hint = BundleHintBuilder.WorkloadMissing();

        hint.Should().Contain($"func workload install {IInstalledBundleWorkloads.BundleWorkloadPackageId}");
        hint.Should().NotContain("func setup");
    }

    [Fact]
    public void WorkloadMissing_KnownRuntime_AppendsSetupFeatureHint()
    {
        string hint = BundleHintBuilder.WorkloadMissing(workerRuntime: "go");

        hint.Should().Contain("func workload install");
        hint.Should().Contain("func setup --features go");
    }

    [Fact]
    public void WorkloadMissing_UnknownRuntime_DoesNotAppendSetupHint()
    {
        string hint = BundleHintBuilder.WorkloadMissing(workerRuntime: "ruby");

        hint.Should().Contain("func workload install");
        hint.Should().NotContain("func setup");
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

        hint.Should().Contain("func setup --features node");
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

        hint.Should().NotContain("func setup");
    }
}
