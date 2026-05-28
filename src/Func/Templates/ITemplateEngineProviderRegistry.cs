// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// DI-resolvable lookup over the registered <see cref="ITemplateEngineProvider"/>
/// instances, keyed by <see cref="ITemplateEngineProvider.EngineId"/>. The
/// orchestrator uses this to dispatch a chosen <see cref="FunctionTemplateInfo"/>
/// to the matching engine.
/// </summary>
internal interface ITemplateEngineProviderRegistry
{
    /// <summary>
    /// Every registered engine provider in registration order.
    /// </summary>
    public IReadOnlyList<ITemplateEngineProvider> Providers { get; }

    /// <summary>
    /// Returns the provider for <paramref name="engineId"/> (case-insensitive),
    /// or <c>null</c> when no provider is registered for that id.
    /// </summary>
    public ITemplateEngineProvider? TryGet(string engineId);
}
