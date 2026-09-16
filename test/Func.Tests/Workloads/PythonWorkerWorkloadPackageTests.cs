// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests.Workloads;

public sealed class PythonWorkerWorkloadPackageTests
{
    [Theory]
    [InlineData("win-x64", "Azure.Functions.Cli.Workloads.Workers.Python.win-x64")]
    [InlineData("LINUX-X64", "Azure.Functions.Cli.Workloads.Workers.Python.linux-x64")]
    [InlineData(" osx-arm64 ", "Azure.Functions.Cli.Workloads.Workers.Python.osx-arm64")]
    public void FromRuntimeIdentifier_AppendsLowercasedRid(string runtimeIdentifier, string expected)
    {
        PythonWorkerWorkloadPackage.FromRuntimeIdentifier(runtimeIdentifier).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromRuntimeIdentifier_RejectsBlankRid(string runtimeIdentifier)
    {
        FluentActions.Invoking(() => PythonWorkerWorkloadPackage.FromRuntimeIdentifier(runtimeIdentifier)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void FromRuntimeIdentifier_RejectsNullRid()
    {
        FluentActions.Invoking(() => PythonWorkerWorkloadPackage.FromRuntimeIdentifier(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void CurrentPackageId_StartsWithPrefix()
    {
        PythonWorkerWorkloadPackage.CurrentPackageId.Should().StartWith(PythonWorkerWorkloadPackage.PackageIdPrefix);
    }

    [Fact]
    public void PackageId_IsRidPointerPackageId()
    {
        PythonWorkerWorkloadPackage.PackageId.Should().Be("Azure.Functions.Cli.Workloads.Workers.Python");
    }
}
