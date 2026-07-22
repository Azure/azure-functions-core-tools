// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Workloads.Sdk.Tests;

public class HelpersTests
{
    [Fact]
    public void MetadataLoadContext_Create_ReturnsUsableContext()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        asm.Should().NotBeNull();
        asm.GetName().Name.Should().Be("Azure.Functions.Cli.Workloads.DotNet");
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
        attributes.Should().NotBeEmpty();
    }

    [Fact]
    public void IsCliWorkloadAttribute_WorkloadAssembly_FindsAttribute()
    {
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Workloads.DotNet");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        bool found = asm.GetCustomAttributesData().Any(a => a.IsCliWorkloadAttribute());

        found.Should().BeTrue();
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

        nonWorkloadAttrs.Should().AllSatisfy(a => a.IsCliWorkloadAttribute().Should().BeFalse());
    }

    [Fact]
    public void IsCliWorkloadAttribute_AbstractionsAssembly_ReturnsFalse()
    {
        // Abstractions doesn't have a CliWorkloadAttribute — verify no false positives.
        string assemblyPath = TestHelpers.GetProjectAssemblyPath("Abstractions");

        using var mlc = MetadataLoadContext.Create(assemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

        bool found = asm.GetCustomAttributesData().Any(a => a.IsCliWorkloadAttribute());

        found.Should().BeFalse();
    }
}
