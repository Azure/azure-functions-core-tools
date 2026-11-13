// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides func-owned TemplateEngine components.
/// </summary>
internal static class FuncTemplateComponents
{
    /// <summary>
    /// Combines built-in engine components with func components.
    /// </summary>
    /// <param name="components">The components supplied by dependency injection.</param>
    /// <returns>The components to register with the template engine host.</returns>
    public static IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> Get(
        IEnumerable<ITemplateComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        return
        [
            ..Microsoft.TemplateEngine.Edge.Components.AllComponents,
            ..Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents,
            ..GetFuncComponents(components),
        ];
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a template component with singleton lifetime.
        /// </summary>
        /// <typeparam name="TInterface">The interface used to resolve the component in the template engine.</typeparam>
        /// <typeparam name="TImplementation">The identified component implementation.</typeparam>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddTemplateComponent<TInterface, TImplementation>()
            where TImplementation : IIdentifiedComponent, TInterface
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ITemplateComponent, Component<TInterface>>(sp =>
            {
                if (sp.GetService<TImplementation>() is { } implementation)
                {
                    return new Component<TInterface>(implementation, dispose: false);
                }

                return new Component<TInterface>(
                    ActivatorUtilities.CreateInstance<TImplementation>(sp),
                    dispose: true);
            });
            return services;
        }

        /// <summary>
        /// Registers all templating related services and components.
        /// </summary>
        /// <returns>The service collection, with services added.</returns>
        public IServiceCollection AddTemplating()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<ITemplaterFactory, TemplaterFactory>();
            services.TryAddSingleton<IWorkloadTemplateSource, WorkloadTemplateSource>();
            services.AddTemplateComponent<ITemplatePackageProviderFactory, WorkloadTemplatePackageProviderFactory>();
            return services;
        }
    }

    private static IEnumerable<(Type Type, IIdentifiedComponent Instance)> GetFuncComponents(
        IEnumerable<ITemplateComponent> components)
    {
        foreach (ITemplateComponent component in components)
        {
            yield return (component.InterfaceType, component.Instance);
        }
    }

    private sealed class Component<TInterface>(IIdentifiedComponent implementation, bool dispose)
        : ITemplateComponent, IAsyncDisposable
    {
        public Type InterfaceType => typeof(TInterface);

        public IIdentifiedComponent Instance { get; } = implementation ?? throw new ArgumentNullException(nameof(implementation));

        public ValueTask DisposeAsync()
        {
            if (!dispose)
            {
                return ValueTask.CompletedTask;
            }

            if (Instance is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            if (Instance is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
