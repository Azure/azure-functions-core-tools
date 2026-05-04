// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Discovery;

/// <summary>
/// Locks the on-the-wire shape of <see cref="CliWorkloadAttribute{T}"/>.
/// The attribute is a public contract used by every workload package author,
/// so changes here are breaking.
/// </summary>
public class CliWorkloadAttributeTests
{
    [Fact]
    public void Attribute_TargetsAssembly_DisallowsMultiple_AndIsNotInherited()
    {
        var usage = Assert.Single(typeof(CliWorkloadAttribute<>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false))
            as AttributeUsageAttribute;

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Assembly, usage!.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void Attribute_GenericParameter_IsConstrainedToIWorkload()
    {
        var typeParam = typeof(CliWorkloadAttribute<>).GetGenericArguments().Single();
        var constraints = typeParam.GetGenericParameterConstraints();

        Assert.Contains(typeof(IWorkload), constraints);
    }
}
