// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads.Sdk;

internal static class Helpers
{
    private const string AbstractionsAssemblyName = "Azure.Functions.Cli.Abstractions.dll";
    private const string CliWorkloadTypeName = "Azure.Functions.Cli.Workloads.CliWorkloadAttribute`1";

    private static readonly string _abstractionsAssembly = Path.Combine(
        Path.GetDirectoryName(typeof(Helpers).Assembly.Location),
        AbstractionsAssemblyName);

    extension(CustomAttributeData attribute)
    {
        public bool IsCliWorkloadAttribute()
        {
            try
            {
                Type type = attribute.AttributeType;
                return type.IsGenericType
                    && type.GetGenericTypeDefinition().FullName
                        == CliWorkloadTypeName;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                return false;
            }
        }
    }

    extension(MetadataLoadContext)
    {
        public static MetadataLoadContext Create(string targetAssembly)
        {
            string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            string[] runtimeAssemblies = Directory.GetFiles(runtimeDir, "*.dll");
            IEnumerable<string> paths = runtimeAssemblies.Concat([targetAssembly, _abstractionsAssembly]);
            PathAssemblyResolver resolver = new(paths);
            return new(resolver);
        }
    }

    extension(Exception exception)
    {
        public bool IsFatal()
        {
            while (exception is not null)
            {
                if (exception
                    is (OutOfMemoryException and not InsufficientMemoryException)
                    or AppDomainUnloadedException
                    or BadImageFormatException
                    or CannotUnloadAppDomainException
                    or InvalidProgramException
                    or AccessViolationException)
                {
                    return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }
    }
}
