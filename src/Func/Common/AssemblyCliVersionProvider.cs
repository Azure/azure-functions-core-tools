// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Production <see cref="ICliVersionProvider"/> backed by the executing
/// assembly's <see cref="AssemblyInformationalVersionAttribute"/>. Values
/// are computed once at construction.
/// </summary>
internal sealed class AssemblyCliVersionProvider : ICliVersionProvider
{
    public static readonly AssemblyCliVersionProvider Instance = new();

    public AssemblyCliVersionProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        InformationalVersion = informational ?? "unknown";
        Version = informational?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    public string Version { get; }

    public string InformationalVersion { get; }

#if PREVIEW
    public bool IsPrerelease => true;
#else
    public bool IsPrerelease => false;
#endif
}
