// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Functions.Cli.Templates.PackageIntake;

internal static class TemplatePackageServiceCollectionExtensions
{
    /// <summary>
    /// Registers bounded local intake without activating a command or selecting production limits.
    /// </summary>
    public static IServiceCollection AddTemplatePackageIntake(this IServiceCollection services, TemplatePackageIntakeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(limits);
        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton(limits);
        services.AddSingleton<LocalTemplatePackageIntake>();
        return services;
    }
}
