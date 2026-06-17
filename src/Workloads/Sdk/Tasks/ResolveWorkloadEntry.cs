// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
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
        static bool IsCliWorkloadAttribute(CustomAttributeData attribute)
        {
            Type type = attribute.AttributeType;
            return type.IsGenericType
                && type.GetGenericTypeDefinition().FullName
                    == "Azure.Functions.Cli.Workloads.CliWorkloadAttribute`1";
        }

        static string GetAbstractionsAssembly(string inputAssembly)
        {
            // We assume all workloads are built with this dependency (otherwise they wouldn't have the required attributes).
            return Path.Combine(Path.GetDirectoryName(inputAssembly), "Azure.Functions.Cli.Abstractions.dll");
        }

        string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        string[] runtimeAssemblies = Directory.GetFiles(runtimeDir, "*.dll");
        IEnumerable<string> paths = runtimeAssemblies.Concat([ AssemblyPath, GetAbstractionsAssembly(AssemblyPath) ]);
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
