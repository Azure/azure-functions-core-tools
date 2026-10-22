// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

internal static class NativeTemplateWorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Composes explicitly supplied CLI-owned providers without changing public commands or boot registration.
    /// </summary>
    public static IServiceCollection AddNativeTemplateWorkflowComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<NativeTemplateWorkflowRegistry>();
        services.AddScoped<TemplateProjectSupport>();
        services.AddScoped<Func<TemplateProjectSupport>>(provider => provider.GetRequiredService<TemplateProjectSupport>);
        services.AddScoped<INewExecutionRunner, NewExecutionRunner>();
        services.AddScoped<IInitExecutionRunner, InitExecutionRunner>();
        services.AddScoped<NativeTemplateWorkflowComposer>();
        return services;
    }

    public static IServiceCollection AddNativeTemplateWorkflowComposition(this IServiceCollection services,
        NativeTemplateWorkloadCapture capture, bool enableBundleBindingConstraints = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capture);
        services.AddSingleton(capture);
        services.AddSingleton(provider => new InstalledProjectStackCatalog(capture.SelectConfiguredStacks(
            provider.GetServices<IProjectStack>().ToArray(), services)));
        services.AddScoped<INativeTemplateAvailability, NativeTemplateAvailability>();
        services.AddSingleton<TemplateEngineBundleContextReader>();
        services.AddScoped<INewExecutionContextResolver>(provider => new NativeTemplateContextResolver(
            provider.GetRequiredService<IFileSystem>(), provider.GetRequiredService<IFunctionsProjectResolver>(),
            provider.GetRequiredService<IProfileResolver>(), provider.GetRequiredService<IOptionsMonitor<StackOptions>>(),
            provider.GetRequiredService<InstalledProjectStackCatalog>(), provider.GetRequiredService<IHostJsonBundleSectionReader>(),
            provider.GetRequiredService<IExtensionBundleResolver>(), provider.GetRequiredService<TemplateEngineBundleContextReader>(),
            enableBundleBindingConstraints));
        return services.AddNativeTemplateWorkflowComposition();
    }
}
