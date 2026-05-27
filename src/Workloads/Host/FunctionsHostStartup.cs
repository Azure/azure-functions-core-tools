// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class FunctionsHostStartup(IConfiguration configuration, bool enableAuth, ScriptApplicationHostOptions hostOptions) : IStartup
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ScriptApplicationHostOptions _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));

    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        if (enableAuth)
        {
            services.AddWebJobsScriptHostAuthentication();
        }
        else
        {
            services.AddAuthentication()
                .AddScriptJwtBearer()
                .AddScheme<AuthenticationLevelOptions, FunctionsHostAuthenticationHandler<AuthenticationLevelOptions>>(
                    AuthLevelAuthenticationDefaults.AuthenticationScheme,
                    configureOptions: _ => { });

            services.AddSingleton<IAuthorizationHandler, FunctionsHostAuthorizationHandler>();
        }

        services.AddWebJobsScriptHostAuthorization();
        services.AddMvc()
            .AddApplicationPart(typeof(HostController).Assembly);

        services.AddWebJobsScriptHost(_configuration);
        services.Configure<ScriptApplicationHostOptions>(options =>
        {
            options.ScriptPath = _hostOptions.ScriptPath;
            options.LogPath = _hostOptions.LogPath;
            options.IsSelfHost = _hostOptions.IsSelfHost;
            options.SecretsPath = _hostOptions.SecretsPath;
        });

        services.AddSingleton<IDependencyValidator, ThrowingDependencyValidator>();

        return services.BuildServiceProvider();
    }

    public void Configure(IApplicationBuilder app)
        => app.UseWebJobsScriptHost();

    private sealed class ThrowingDependencyValidator : DependencyValidator
    {
        public override void Validate(IServiceCollection services)
        {
            try
            {
                base.Validate(services);
            }
            catch (InvalidHostServicesException ex)
            {
                throw new InvalidOperationException("Invalid host services.", ex);
            }
        }
    }
}
