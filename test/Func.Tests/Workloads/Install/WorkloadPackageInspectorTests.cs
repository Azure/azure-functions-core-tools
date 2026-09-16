// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadPackageInspectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workload-inspector-").FullName;
    private readonly WorkloadPackageInspector _inspector;

    public WorkloadPackageInspectorTests()
    {
        IWorkloadRuntimeIdentifierProvider runtimeIdentifierProvider = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtimeIdentifierProvider.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtimeIdentifierProvider);
        _inspector = new WorkloadPackageInspector(Substitute.For<IWorkloadMetadataReader>(), selector);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MatchesIdentity_UnreadablePackage_ReturnsFalse()
    {
        string path = Path.Combine(_root, "not-a-package.nupkg");
        File.WriteAllText(path, "invalid");

        bool matches = _inspector.MatchesIdentity(path, "example.workload", "1.0.0");

        matches.Should().BeFalse();
    }

    [Fact]
    public void ValidateIdentity_MismatchedPackage_Throws()
    {
        WorkloadPackageIdentity identity = new("other.workload", "2.0.0", [], [], [], null, null);

        Action act = () => _inspector.ValidateIdentity(identity, "example.workload", "1.0.0");

        act.Should().ThrowExactly<InvalidWorkloadException>()
            .WithMessage("*example.workload*1.0.0*other.workload*2.0.0*");
    }
}
