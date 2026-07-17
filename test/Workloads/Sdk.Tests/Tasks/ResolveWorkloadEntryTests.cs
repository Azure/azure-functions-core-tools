// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NSubstitute;
using Microsoft.Build.Framework;
using Azure.Functions.Cli.Workloads.Sdk.Tests;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks.Tests;

public class ResolveWorkloadEntryTests
{
    [Fact]
    public void ScanWorkloadEntry_Success()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");
        ResolveWorkloadEntry resolveWorkloadEntry = new()
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            AssemblyPath = assemblyPath
        };

        resolveWorkloadEntry.Execute();

        resolveWorkloadEntry.WorkloadType.Should().Be("Azure.Functions.Cli.Workloads.DotNet.DotNetWorkload");
    }


    [Fact]
    public void ScanWorkloadEntry_NotFound()
    {
        string assemblyPath = typeof(ResolveWorkloadEntry).Assembly.Location;
        ResolveWorkloadEntry resolveWorkloadEntry = new()
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            AssemblyPath = assemblyPath
        };

        resolveWorkloadEntry.Execute();

        resolveWorkloadEntry.WorkloadType.Should().BeEmpty();
    }
}
