﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.HostActions.WebHost.Security;
using Azure.Functions.Cli.Diagnostics;
using Azure.Functions.Cli.ExtensionBundle;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Actions.HostActions
{
    public class Startup : IStartup
    {
        private readonly WebHostBuilderContext _builderContext;
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly string[] _corsOrigins;
        private readonly bool _corsCredentials;
        private readonly bool _enableAuth;
        private readonly string _userSecretsId;
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly string _jsonOutputFile;

        public Startup(
            WebHostBuilderContext builderContext,
            ScriptApplicationHostOptions hostOptions,
            string corsOrigins,
            bool corsCredentials,
            bool enableAuth,
            string userSecretsId,
            LoggingFilterHelper loggingFilterHelper,
            string jsonOutputFile)
        {
            _builderContext = builderContext;
            _hostOptions = hostOptions;
            _enableAuth = enableAuth;
            _userSecretsId = userSecretsId;
            _loggingFilterHelper = loggingFilterHelper;
            _jsonOutputFile = jsonOutputFile;
            if (!string.IsNullOrEmpty(corsOrigins))
            {
                _corsOrigins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries);
                _corsCredentials = corsCredentials;
            }
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            if (_corsOrigins != null)
            {
                services.AddCors();
            }

            if (_enableAuth)
            {
                services.AddWebJobsScriptHostAuthentication();
            }
            else
            {
                services.AddAuthentication()
                    .AddScriptJwtBearer()
                    .AddScheme<AuthenticationLevelOptions, CliAuthenticationHandler<AuthenticationLevelOptions>>(AuthLevelAuthenticationDefaults.AuthenticationScheme, configureOptions: _ => { });

                // Bypass all local auth
                services.AddSingleton<IAuthorizationHandler, CoreToolsAuthorizationHandler>();
            }

            services.AddWebJobsScriptHostAuthorization();

            services.AddMvc()
                .AddApplicationPart(typeof(HostController).Assembly);

            // workaround for https://github.com/Azure/azure-functions-core-tools/issues/2097
            SetBundlesEnvironmentVariables();

            services.AddWebJobsScriptHost(_builderContext.Configuration);

            services.Configure<ScriptApplicationHostOptions>(o =>
            {
                o.ScriptPath = _hostOptions.ScriptPath;
                o.LogPath = _hostOptions.LogPath;
                o.IsSelfHost = _hostOptions.IsSelfHost;
                o.SecretsPath = _hostOptions.SecretsPath;
            });

            services.AddSingleton<IConfigureBuilder<IConfigurationBuilder>>(_ => new ExtensionBundleConfigurationBuilder(_hostOptions));
            services.AddSingleton<IConfigureBuilder<IConfigurationBuilder>, DisableConsoleConfigurationBuilder>();
            services.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(_ => new LoggingBuilder(_loggingFilterHelper, _jsonOutputFile));
            if (!string.IsNullOrEmpty(_userSecretsId))
            {
                services.AddSingleton<IConfigureBuilder<IConfigurationBuilder>>((provider) => new UserSecretsConfigurationBuilder(_userSecretsId, _loggingFilterHelper, provider.GetService<IOptions<LoggerFilterOptions>>().Value));
            }

            services.AddSingleton<IDependencyValidator, ThrowingDependencyValidator>();

            return services.BuildServiceProvider();
        }

        private void SetBundlesEnvironmentVariables()
        {
            var bundleId = ExtensionBundleHelper.GetExtensionBundleOptions(_hostOptions).Id;
            if (!string.IsNullOrEmpty(bundleId))
            {
                Environment.SetEnvironmentVariable("AzureFunctionsJobHost__extensionBundle__downloadPath", ExtensionBundleHelper.GetBundleDownloadPath(bundleId));
                Environment.SetEnvironmentVariable("AzureFunctionsJobHost__extensionBundle__ensureLatest", "true");
            }
        }

        public void Configure(IApplicationBuilder app)
        {
            if (_corsOrigins != null)
            {
                app.UseCors(builder =>
                {
                    var origins = builder.WithOrigins(_corsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    if (_corsCredentials)
                    {
                        origins.AllowCredentials();
                    }
                });
            }
#pragma warning disable CS0618 // IApplicationLifetime is obsolete
            IApplicationLifetime applicationLifetime = app.ApplicationServices
                .GetRequiredService<IApplicationLifetime>();

            app.UseWebJobsScriptHost(applicationLifetime);
#pragma warning restore CS0618 // Type is obsolete
        }

        private class ThrowingDependencyValidator : DependencyValidator
        {
            public override void Validate(IServiceCollection services)
            {
                try
                {
                    base.Validate(services);
                }
                catch (InvalidHostServicesException ex)
                {
                    // Rethrow this as an InvalidOperationException to bypass the handling
                    // in the host. This will stop invalid services in the CLI only.
                    throw new InvalidOperationException("Invalid host services.", ex);
                }
            }
        }
    }
}
