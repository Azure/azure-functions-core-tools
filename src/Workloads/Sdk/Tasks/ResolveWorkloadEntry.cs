// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks;

public sealed class ResolveWorkloadEntry : Microsoft.Build.Utilities.Task
{
    private static readonly string _abstractionsAssembly = Path.Combine(
        Path.GetDirectoryName(typeof(ResolveWorkloadEntry).Assembly.Location),
        "Azure.Functions.Cli.Abstractions.dll");

    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    [Output]
    public string WorkloadType { get; private set; } = string.Empty;

    public override bool Execute()
    {
        static bool IsCliWorkloadAttribute(CustomAttributeData attribute)
        {
            Type type = attribute.AttributeType;
            return type.IsGenericType
                && type.GetGenericTypeDefinition().FullName
                    == "Azure.Functions.Cli.Workloads.CliWorkloadAttribute`1";
        }

        string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        string[] runtimeAssemblies = Directory.GetFiles(runtimeDir, "*.dll");
        IEnumerable<string> paths = runtimeAssemblies.Concat([ AssemblyPath, _abstractionsAssembly ]);
        var resolver = new PathAssemblyResolver(paths);

        using MetadataLoadContext mlc = new(resolver);
        Assembly asm = mlc.LoadFromAssemblyPath(AssemblyPath);

        foreach (CustomAttributeData? attribute in asm.GetCustomAttributesData())
        {
            Log.LogMessage(MessageImportance.High, $"Found custom attribute: {attribute.AttributeType.FullName}");

            if (IsCliWorkloadAttribute(attribute))
            {
                WorkloadType = attribute.AttributeType.GetGenericArguments()[0].FullName;
                break;
            }
        }

        return true;
    }
}
