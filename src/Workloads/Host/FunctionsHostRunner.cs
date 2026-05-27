// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class FunctionsHostRunner : IFunctionsHostRunner
{
    public async Task RunAsync(string[] args, bool enableAuth, CancellationToken cancellationToken)
    {
        ScriptApplicationHostOptions hostOptions = CreateHostOptions(Environment.CurrentDirectory);

        using IWebHost host = WebHost.CreateDefaultBuilder(args)
            .UseSetting(WebHostDefaults.ApplicationKey, typeof(FunctionsHostStartup).Assembly.GetName().Name)
            .ConfigureAppConfiguration(configBuilder =>
            {
                configBuilder.AddEnvironmentVariables();
            })
            .ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddHostStructuredLogging();
                RawHostLogCaptureProvider.AddIfEnabled(loggingBuilder, context.Configuration);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IStartup>(new FunctionsHostStartup(context.Configuration, enableAuth, hostOptions));
                services.AddSingleton<IDiagnosticEventRepository, DiagnosticEventNullRepository>();
                services.AddSingleton<IConfigureBuilder<ILoggingBuilder>, HostStructuredScriptLoggingBuilder>();
            })
            .Build();

        RegisterFunctionMetadataEmission(host.Services);
        await host.RunAsync(cancellationToken);
    }

    private static void RegisterFunctionMetadataEmission(IServiceProvider services)
    {
        IHostApplicationLifetime? lifetime = services.GetService<IHostApplicationLifetime>();
        if (lifetime is null)
        {
            return;
        }

        lifetime.ApplicationStarted.Register(static state =>
        {
            var services = (IServiceProvider)state!;
            HostFunctionMetadataEmitter.EmitSnapshot(services);
        }, services);
    }

    private static ScriptApplicationHostOptions CreateHostOptions(string scriptPath)
        => new()
        {
            IsSelfHost = true,
            ScriptPath = scriptPath,
            LogPath = Path.Combine(Path.GetTempPath(), "LogFiles", "Application", "Functions"),
            SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets"),
        };
}
