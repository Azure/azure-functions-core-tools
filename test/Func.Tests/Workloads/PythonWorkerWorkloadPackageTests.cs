// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public sealed class PythonWorkerWorkloadPackageTests
{
    [Theory]
    [InlineData("win-x64", "Azure.Functions.Cli.Workloads.Workers.Python.win-x64")]
    [InlineData("LINUX-X64", "Azure.Functions.Cli.Workloads.Workers.Python.linux-x64")]
    [InlineData(" osx-arm64 ", "Azure.Functions.Cli.Workloads.Workers.Python.osx-arm64")]
    public void FromRuntimeIdentifier_AppendsLowercasedRid(string runtimeIdentifier, string expected)
    {
        Assert.Equal(expected, PythonWorkerWorkloadPackage.FromRuntimeIdentifier(runtimeIdentifier));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromRuntimeIdentifier_RejectsBlankRid(string runtimeIdentifier)
    {
        Assert.Throws<ArgumentException>(() => PythonWorkerWorkloadPackage.FromRuntimeIdentifier(runtimeIdentifier));
    }

    [Fact]
    public void FromRuntimeIdentifier_RejectsNullRid()
    {
        Assert.Throws<ArgumentNullException>(() => PythonWorkerWorkloadPackage.FromRuntimeIdentifier(null!));
    }

    [Fact]
    public void CurrentPackageId_StartsWithPrefix()
    {
        Assert.StartsWith(PythonWorkerWorkloadPackage.PackageIdPrefix, PythonWorkerWorkloadPackage.CurrentPackageId);
    }

    [Theory]
    [InlineData("win-x64", true)]
    [InlineData("linux-x64", true)]
    [InlineData("linux-arm64", true)]
    [InlineData("osx-x64", true)]
    [InlineData("osx-arm64", true)]
    [InlineData("WIN-X64", true)]
    [InlineData("win-arm64", false)]
    [InlineData("freebsd-x64", false)]
    public void IsSupported_MatchesPublishedRids(string runtimeIdentifier, bool expected)
    {
        Assert.Equal(expected, PythonWorkerWorkloadPackage.IsSupported(runtimeIdentifier));
    }
}
