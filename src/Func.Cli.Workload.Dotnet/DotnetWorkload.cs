// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Entry point for the dotnet workload. Registers the dotnet
/// <see cref="IProjectInitializer"/> with the host so <c>func init</c>
/// picks it up.
/// </summary>
public sealed class DotnetWorkload : IWorkload
{
    public string PackageId => "Azure.Functions.Cli.Workload.Dotnet";

    public string PackageVersion => "1.0.0";

    public WorkloadType Type => WorkloadType.Stack;

    public string DisplayName => "DotNet";

    public string Description => "C# and F# project support for Azure Functions.";

    public void Configure(IFunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<IProjectInitializer, DotnetProjectInitializer>();
    }
}
