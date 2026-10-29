// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Commands.Templates;
using Azure.Functions.Cli.Templates.Integration.Native;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Selection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Functions.Cli.Templates.Integration;

internal static class PackageTemplateServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application with caller-selected intake limits, without choosing an adapter or activating commands.
    /// </summary>
    public static IServiceCollection AddPackageTemplateApplication(this IServiceCollection services, TemplatePackageIntakeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(limits);
        services.AddTemplatePackageIntake(limits);
        services.TryAddSingleton<TemplateArgumentParser>();
        services.TryAddSingleton<TemplateSelector>();
        services.TryAddSingleton<ItemTemplateCompatibilityMatcher>();
        services.TryAddSingleton<PackageTemplateApplication>();
        return services;
    }

    /// <summary>
    /// Registers production private native adaptation without changing the public command graph.
    /// </summary>
    public static IServiceCollection AddNativePackageTemplates(this IServiceCollection services, TemplatePackageIntakeLimits limits)
    {
        services.AddPackageTemplateApplication(limits);
        services.TryAddSingleton<InstalledProjectStackCatalog>();
        services.TryAddSingleton<IPackageTemplateAdapter, NativePackageTemplateAdapter>();
        services.TryAddSingleton<PackageTemplateCommandExecutor>();
        services.AddTransient<PackageTemplateInitCommand>();
        services.AddTransient<PackageTemplateNewCommand>();
        services.AddTransient<PackageTemplateCandidateGraph>();
        return services;
    }
}
