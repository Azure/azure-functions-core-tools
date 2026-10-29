// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates.Integration;

internal static class PublicPackageTemplateServiceCollectionExtensions
{
    public static IServiceCollection AddPublicPackageTemplates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNativePackageTemplates(new(1024 * 1024, 1024, 8 * 1024 * 1024, 1024 * 1024, 64 * 1024, 4096, 1024, 16 * 1024 * 1024, 8));
        services.AddSingleton<IOptions<PublicTemplatePackageOptions>>(Options.Create(CreateOptions()));
        services.AddSingleton<ITemplatePackageResources>(_ => new TemplatePackageResources(typeof(PublicPackageTemplates).Assembly));
        services.AddSingleton<IPublicPackageTemplates, PublicPackageTemplates>();
        return services;
    }

    private static PublicTemplatePackageOptions CreateOptions()
    {
#if TEMPLATES_QUALIFICATION
        return new()
        {
            Qualification = new(
                new("Azure.Functions.Templates.Node.Basic", "0.1.0-qualification.1", "FuncTemplate",
                    "3af83804e4fa033a96530c952d0110aebe0c3ade0c171c4c2e0d06de79eec6cc"),
                "Azure.Functions.Cli.Templates.Qualification.Node.Basic.nupkg",
                "Azure.Functions.Templates.Node.Basic.Project.JavaScript",
                "Azure.Functions.Templates.Node.Basic.Http.JavaScript",
                "HttpTrigger-JavaScript"),
        };
#else
        return new();
#endif
    }
}
