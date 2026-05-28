// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting.FirstRun;

internal static class FirstRunRegistration
{
    public static IServiceCollection AddFirstRunExperience(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFirstRunStateStore, FileFirstRunStateStore>();
        services.AddSingleton<IFirstRunCoordinator, FirstRunCoordinator>();
        return services;
    }
}
