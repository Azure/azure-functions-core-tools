// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Microsoft.Build.Framework;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks;

public sealed class ResolveWorkloadEntry : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    [Output]
    public string WorkloadType { get; private set; } = string.Empty;

    public override bool Execute()
    {
        using var mlc = MetadataLoadContext.Create(AssemblyPath);
        Assembly asm = mlc.LoadFromAssemblyPath(AssemblyPath);

        foreach (CustomAttributeData? attribute in asm.GetCustomAttributesData())
        {
            if (attribute.IsCliWorkloadAttribute())
            {
                WorkloadType = attribute.AttributeType.GetGenericArguments()[0].FullName;
                break;
            }
        }

        return true;
    }
}
