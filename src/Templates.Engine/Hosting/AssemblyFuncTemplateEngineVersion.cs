// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Production <see cref="IFuncTemplateEngineVersion"/> backed by this
/// assembly's <see cref="AssemblyInformationalVersionAttribute"/>, which the
/// build stamps from the same version props as the rest of the CLI. Registered
/// with <c>TryAdd</c> so the composition root can bridge the running CLI
/// version instead.
/// </summary>
internal sealed class AssemblyFuncTemplateEngineVersion : IFuncTemplateEngineVersion
{
    public AssemblyFuncTemplateEngineVersion()
    {
        Assembly assembly = typeof(AssemblyFuncTemplateEngineVersion).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Strip build metadata (+sha) so the value stays a stable, path-safe
        // directory segment across builds of the same release.
        Version = informational?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    public string Version { get; }
}
