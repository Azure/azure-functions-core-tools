// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Templates.PowerShell;
using Microsoft.Extensions.DependencyInjection;
using DotNetShared = Azure.Functions.Cli.Templates.DotNet.Shared;
using GoShared = Azure.Functions.Cli.Templates.Go.Shared;
using PowerShellShared = Azure.Functions.Cli.Templates.PowerShell.Shared;

namespace Azure.Functions.Cli.Templates;

internal static class FirstPartyNativeWorkflowRegistration
{
    public static void AddFirstPartyNativeWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Workloads.Node.NodeNpmInstaller>();
        services.AddScoped<NodeTemplateWorkflowProvider>();
        services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
        services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
        services.AddSingleton<DotNetShared.IDotnetPathResolver, DotNetShared.DotnetPathResolver>();
        services.AddScoped<DotNetShared.IDotnetCliRunner, DotNetShared.DotnetCliRunner>();
        services.AddScoped<DotNetShared.FSharpProjectFiles>();
        services.AddScoped<DotNetTemplateWorkflowComposer>();
        services.AddScoped(provider => provider.GetRequiredService<DotNetTemplateWorkflowComposer>().Create());
        services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
        services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
        services.AddSingleton<GoShared.GoProjectPreparation>();
        services.AddScoped<GoNativeProjectPreparation>();
        services.AddScoped<INativeProjectWorkflowProvider, GoProjectWorkflowProvider>();
        services.AddSingleton<PowerShellShared.PowerShellProjectPreparation>();
        services.AddScoped<PowerShellNativeProjectPreparation>();
        services.AddScoped<INativeProjectWorkflowProvider, PowerShellProjectWorkflowProvider>();
        services.AddScoped<INativeProjectWorkflowProvider, JavaProjectWorkflowProvider>();
    }
}
