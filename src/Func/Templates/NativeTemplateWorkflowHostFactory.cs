// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Workloads.Invocation;
using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Runtime.ExceptionServices;

namespace Azure.Functions.Cli.Templates;

internal interface INativeTemplateWorkflowHostFactory
{
    public Task<NativeTemplateWorkflowHost> CreateAsync(IInteractionService interaction, WorkloadPathsOptions paths, CliConfigurationPathsOptions configurationPaths,
        Action<IServiceCollection> configureProviders, bool enableBundleBindingConstraints, CancellationToken cancellationToken,
        bool includeProjectConfiguration = true, bool deferProjectConfigurationErrors = false, IReadOnlyList<string>? arguments = null);
}

/// <summary>
/// Builds the CLI host, capturing workload outcomes before native provider construction.
/// </summary>
internal sealed class NativeTemplateWorkflowHostFactory : INativeTemplateWorkflowHostFactory
{
    public async Task<NativeTemplateWorkflowHost> CreateAsync(IInteractionService interaction, WorkloadPathsOptions paths, CliConfigurationPathsOptions configurationPaths,
        Action<IServiceCollection> configureProviders, bool enableBundleBindingConstraints, CancellationToken cancellationToken,
        bool includeProjectConfiguration = true, bool deferProjectConfigurationErrors = false, IReadOnlyList<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurationPaths);
        ArgumentNullException.ThrowIfNull(configureProviders);
        cancellationToken.ThrowIfCancellationRequested();
        ExceptionDispatchInfo? projectConfigurationFailure = null;
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction, includeProjectConfiguration,
            deferProjectConfigurationErrors ? exception => projectConfigurationFailure = ExceptionDispatchInfo.Capture(exception) : null);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<IWorkloadPaths>(paths);
        builder.Services.AddSingleton(configurationPaths);
        builder.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configurationPaths));
        NativeTemplateWorkloadCapture capture = new();
        builder.Services.AddSingleton(capture);
        if (arguments is not null && IsWorkloadManagementCommand(builder, arguments))
        {
            await WorkloadRegistration.RegisterWorkloadMetadataAsync(builder.Services, paths, interaction, capture, cancellationToken);
        }
        else
        {
            await WorkloadRegistration.RegisterWorkloadsAsync(builder.Services, paths, interaction, capture, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        builder.Services.AddNativeTemplateWorkflowComposition(capture, enableBundleBindingConstraints);
        configureProviders(builder.Services);
        return new(builder.Build(), projectConfigurationFailure);
    }

    internal bool IsWorkloadManagementCommand(HostApplicationBuilder builder, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(arguments);
        IServiceCollection graph = new ServiceCollection();
        foreach (ServiceDescriptor descriptor in builder.Services)
        {
            if (descriptor.ServiceType != typeof(IConfiguration))
            {
                graph.Add(descriptor);
            }
        }
        // The host's factory returns the shared configuration; an instance registration keeps the probe from owning it.
        graph.AddSingleton<IConfiguration>(builder.Configuration);
        graph.AddSingleton<IWorkloadProvider>(new WorkloadProvider([]));
        graph.AddSingleton<IWorkloadInvoker, WorkloadInvoker>();
        using ServiceProvider provider = graph.BuildServiceProvider();
        FuncRootCommand root = Parser.CreateCommand(provider);
        ParseResult parsed = NativeCliEntryPoint.ParseArguments(root, [.. arguments]);
        if (parsed.Errors.Count != 0)
        {
            return false;
        }
        for (SymbolResult? result = parsed.CommandResult; result is not null; result = result.Parent)
        {
            if (result is CommandResult { Command: WorkloadCommand })
            {
                return true;
            }
        }
        if (parsed.CommandResult.Command is HelpCommand help)
        {
            string? requested = parsed.GetValue(help.CommandArgument);
            return root.Subcommands.FirstOrDefault(command =>
                string.Equals(command.Name, requested, StringComparison.OrdinalIgnoreCase)) is WorkloadCommand;
        }
        return false;
    }
}

internal sealed class NativeTemplateWorkflowHost(IHost host, ExceptionDispatchInfo? projectConfigurationFailure = null) : IDisposable
{
    private readonly IHost _host = host ?? throw new ArgumentNullException(nameof(host));

    internal Task StartAsync(CancellationToken cancellationToken) => _host.StartAsync(cancellationToken);

    internal bool HasProjectConfigurationFailure => projectConfigurationFailure is not null;

    internal void ThrowIfProjectConfigurationFailed() => projectConfigurationFailure?.Throw();

    public NativeTemplateWorkflowInvocation CreateInvocation()
    {
        NativeTemplateWorkflowInvocation invocation = CreateCommandScope();
        try
        {
            _ = invocation.Composer;
            return invocation;
        }
        catch
        {
            invocation.Dispose();
            throw;
        }
    }

    internal NativeTemplateWorkflowInvocation CreateCommandScope() => new(_host.Services.CreateScope());

    public void Dispose()
    {
        foreach (RuntimeWorkloadInfo workload in _host.Services.GetRequiredService<IWorkloadProvider>().GetRuntimeWorkloads())
        {
            workload.LoadContext.Unload();
        }

        _host.Dispose();
    }
}

internal sealed class NativeTemplateWorkflowInvocation(IServiceScope scope) : IDisposable
{
    private readonly IServiceScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public NativeTemplateWorkflowComposer Composer => _scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();

    internal IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose() => _scope.Dispose();
}
