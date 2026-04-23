// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Stub dotnet workload for the prototype. Demonstrates the DI registration
/// shape: contributes a project initializer (<c>func init</c>) and a template
/// provider (<c>func new</c>). Real implementation lands in a separate package.
/// </summary>
public sealed class DotnetWorkload : IWorkload
{
    public string Id => "dotnet";

    public string DisplayName => ".NET";

    public string Description => "C# / F# project support.";

    public void Register(IWorkloadBuilder builder)
    {
        builder.AddProjectInitializer<DotnetProjectInitializer>();
        builder.AddTemplateProvider<DotnetTemplateProvider>();
    }
}
