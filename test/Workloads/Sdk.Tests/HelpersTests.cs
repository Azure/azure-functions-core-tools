// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Sdk.Tests;

public class HelpersTests
{
    [Fact]
    public void MetadataLoadContext_Create_ReturnsUsableContext()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        Assert.NotNull(asm);
        Assert.Equal("Azure.Functions.Cli.Workloads.DotNet", asm.GetName().Name);
    }

    [Fact]
    public void MetadataLoadContext_Create_CanResolveAbstractionsAssembly()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        // The workload assembly references Abstractions — resolving its custom
        // attributes requires the Abstractions assembly to be loadable.
        CustomAttributeData[] attributes = [.. asm.GetCustomAttributesData()];
        Assert.NotEmpty(attributes);
    }

    [Fact]
    public void IsCliWorkloadAttribute_WorkloadAssembly_FindsAttribute()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        bool found = asm.GetCustomAttributesData().Any(a => a.IsCliWorkloadAttribute());

        Assert.True(found);
    }

    [Fact]
    public void IsCliWorkloadAttribute_NonWorkloadAttribute_ReturnsFalse()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        // Filter to attributes that are NOT CliWorkloadAttribute
        IEnumerable<CustomAttributeData> nonWorkloadAttrs = asm.GetCustomAttributesData()
            .Where(a => !a.AttributeType.IsGenericType);

        Assert.All(nonWorkloadAttrs, a => Assert.False(a.IsCliWorkloadAttribute()));
    }

    [Fact]
    public void IsCliWorkloadAttribute_AbstractionsAssembly_ReturnsFalse()
    {
        // Abstractions doesn't have a CliWorkloadAttribute — verify no false positives.
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Abstractions");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        bool found = asm.GetCustomAttributesData().Any(a => a.IsCliWorkloadAttribute());

        Assert.False(found);
    }
}
