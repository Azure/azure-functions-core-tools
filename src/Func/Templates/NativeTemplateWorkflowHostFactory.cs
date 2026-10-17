// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Cli.Templates;

internal interface INativeTemplateWorkflowHostFactory
{
    public Task<NativeTemplateWorkflowHost> CreateAsync(IInteractionService interaction, WorkloadPathsOptions paths, CliConfigurationPathsOptions configurationPaths,
        Action<IServiceCollection> configureProviders, bool enableBundleBindingConstraints, CancellationToken cancellationToken);
}

/// <summary>
/// Builds only an explicitly requested native workflow host, capturing workload outcomes before provider construction.
/// </summary>
internal sealed class NativeTemplateWorkflowHostFactory : INativeTemplateWorkflowHostFactory
{
    public async Task<NativeTemplateWorkflowHost> CreateAsync(IInteractionService interaction, WorkloadPathsOptions paths, CliConfigurationPathsOptions configurationPaths,
        Action<IServiceCollection> configureProviders, bool enableBundleBindingConstraints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurationPaths);
        ArgumentNullException.ThrowIfNull(configureProviders);
        cancellationToken.ThrowIfCancellationRequested();
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<IWorkloadPaths>(paths);
        builder.Services.AddSingleton(configurationPaths);
        builder.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configurationPaths));
        NativeTemplateWorkloadCapture capture = new();
        builder.Services.AddSingleton(capture);
        await WorkloadRegistration.RegisterWorkloadsAsync(builder.Services, paths, interaction, capture, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        builder.Services.AddNativeTemplateWorkflowComposition(capture, enableBundleBindingConstraints);
        configureProviders(builder.Services);
        return new(builder.Build());
    }
}

internal sealed class NativeTemplateWorkflowHost(IHost host) : IDisposable
{
    private readonly IHost _host = host ?? throw new ArgumentNullException(nameof(host));

    internal Task StartAsync(CancellationToken cancellationToken) => _host.StartAsync(cancellationToken);

    public NativeTemplateWorkflowInvocation CreateInvocation()
    {
        IServiceScope scope = _host.Services.CreateScope();
        try
        {
            return new(scope, scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (RuntimeWorkloadInfo workload in _host.Services.GetRequiredService<IWorkloadProvider>().GetRuntimeWorkloads())
        {
            workload.LoadContext.Unload();
        }

        _host.Dispose();
    }
}

internal sealed class NativeTemplateWorkflowInvocation(IServiceScope scope, NativeTemplateWorkflowComposer composer) : IDisposable
{
    private readonly IServiceScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public NativeTemplateWorkflowComposer Composer { get; } = composer ?? throw new ArgumentNullException(nameof(composer));

    internal IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose() => _scope.Dispose();
}
