// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Concrete <see cref="ITemplateEngineProviderRegistry"/> backed by the DI
/// container's <c>IEnumerable&lt;ITemplateEngineProvider&gt;</c>. Built on the
/// first call so reflection cost lands once per process.
/// </summary>
internal sealed class TemplateEngineProviderRegistry : ITemplateEngineProviderRegistry
{
    private readonly IReadOnlyList<ITemplateEngineProvider> _providers;
    private readonly Dictionary<string, ITemplateEngineProvider> _byEngineId;

    public TemplateEngineProviderRegistry(IEnumerable<ITemplateEngineProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToList();
        _byEngineId = new Dictionary<string, ITemplateEngineProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (ITemplateEngineProvider provider in _providers)
        {
            if (string.IsNullOrWhiteSpace(provider.EngineId))
            {
                throw new InvalidOperationException(
                    $"Template engine provider '{provider.GetType().FullName}' returned a null or empty EngineId. " +
                    "Every ITemplateEngineProvider must declare a stable, non-empty engine identifier.");
            }

            if (!_byEngineId.TryAdd(provider.EngineId, provider))
            {
                throw new InvalidOperationException(
                    $"Two template engine providers registered for EngineId '{provider.EngineId}': " +
                    $"{_byEngineId[provider.EngineId].GetType().FullName} and {provider.GetType().FullName}. " +
                    "Engine ids must be unique across the CLI process.");
            }
        }
    }

    public IReadOnlyList<ITemplateEngineProvider> Providers => _providers;

    public ITemplateEngineProvider? TryGet(string engineId)
    {
        if (string.IsNullOrWhiteSpace(engineId))
        {
            return null;
        }

        return _byEngineId.TryGetValue(engineId, out ITemplateEngineProvider? provider) ? provider : null;
    }
}
