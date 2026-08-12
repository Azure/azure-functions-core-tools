// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadRidPackageSelectorTests
{
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider =
        Substitute.For<IWorkloadRuntimeIdentifierProvider>();

    public WorkloadRidPackageSelectorTests()
    {
        _runtimeIdentifierProvider.Current.Returns("win-x64");
    }

    [Fact]
    public void SelectImplementation_CurrentRuntimeSupported_ReturnsExactPackage()
    {
        WorkloadRidPackageSelector selector = new(_runtimeIdentifierProvider);
        InspectedWorkloadPackage pointer = Pointer(
            new Dictionary<string, string> { ["win-x64"] = "Example.Workload.Win-X64" });

        WorkloadPointerSelection selection = selector.SelectImplementation(pointer);

        selection.RuntimeIdentifier.Should().Be("win-x64");
        selection.PackageId.Should().Be("example.workload.win-x64");
    }

    [Fact]
    public void SelectImplementation_CurrentRuntimeMissing_ListsSupportedRuntimes()
    {
        WorkloadRidPackageSelector selector = new(_runtimeIdentifierProvider);
        InspectedWorkloadPackage pointer = Pointer(
            new Dictionary<string, string> { ["linux-x64"] = "example.workload.linux-x64" });

        Action act = () => selector.SelectImplementation(pointer);

        act.Should().ThrowExactly<WorkloadPackageNotFoundException>()
            .WithMessage("*win-x64*linux-x64*");
    }

    [Fact]
    public void ValidateImplementation_PackageIdentityDiffers_Throws()
    {
        WorkloadRidPackageSelector selector = new(_runtimeIdentifierProvider);
        InspectedWorkloadPackage pointer = Pointer(
            new Dictionary<string, string> { ["win-x64"] = "example.workload.win-x64" });
        WorkloadPointerSelection selection = selector.SelectImplementation(pointer);
        InspectedWorkloadPackage implementation = Implementation("other.workload.win-x64", "win-x64");

        Action act = () => selector.ValidateImplementation(pointer, selection, implementation);

        act.Should().ThrowExactly<InvalidWorkloadException>()
            .WithMessage("*Pointer implementation mismatch*");
    }

    private static InspectedWorkloadPackage Pointer(IReadOnlyDictionary<string, string> packages)
        => new(
            "pointer.nupkg",
            new WorkloadPackageIdentity("example.workload", "1.0.0", [WorkloadPackageTypes.Workload], [], [], null, null),
            new WorkloadMetadata
            {
                Schema = WorkloadManifestSchema.PackageManifestV1Schema,
                Kind = WorkloadKind.RidPointer,
                Packages = packages,
            },
            WorkloadPackageRole.Pointer);

    private static InspectedWorkloadPackage Implementation(string packageId, string runtimeIdentifier)
        => new(
            "implementation.nupkg",
            new WorkloadPackageIdentity(
                packageId, "1.0.0", [WorkloadPackageTypes.RuntimeIdentifierPackage], [],
                [runtimeIdentifier], null, null),
            new WorkloadMetadata
            {
                Schema = WorkloadManifestSchema.PackageManifestV1Schema,
                Kind = WorkloadKind.Content,
                RuntimeIdentifier = runtimeIdentifier,
            },
            WorkloadPackageRole.RuntimeIdentifierImplementation);
}
