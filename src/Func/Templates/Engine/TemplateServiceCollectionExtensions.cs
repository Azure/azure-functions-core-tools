// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Registers template services and core CLI components.
/// </summary>
internal static class TemplateServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a core template component with singleton lifetime.
        /// </summary>
        /// <typeparam name="TInterface">The interface used to resolve the component in the template engine.</typeparam>
        /// <typeparam name="TImplementation">The identified component implementation.</typeparam>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddTemplateComponent<TInterface, TImplementation>()
            where TImplementation : IIdentifiedComponent, TInterface
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ICoreTemplateComponent, Component<TInterface>>(sp =>
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
            services.TryAddSingleton<IFuncTemplateComponentProvider, FuncTemplateComponentsProvider>();
            services.TryAddSingleton<IWorkloadTemplateSource, WorkloadTemplateSource>();
            services.TryAddSingleton<PostActionDispatcher>();
            services.AddTemplateComponent<PostActionDispatcher, PostActionDispatcher>();
            services.AddTemplateComponent<ITemplatePackageProviderFactory, WorkloadTemplatePackageProviderFactory>();
            return services;
        }
    }

    private sealed class Component<TInterface>(IIdentifiedComponent implementation, bool dispose)
        : ICoreTemplateComponent, IAsyncDisposable
    {
        /// <inheritdoc />
        public Type InterfaceType => typeof(TInterface);

        /// <inheritdoc />
        public IIdentifiedComponent Instance { get; } = implementation ?? throw new ArgumentNullException(nameof(implementation));

        /// <inheritdoc />
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
