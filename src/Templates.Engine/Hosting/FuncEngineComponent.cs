// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// A single engine component contributed by the func host on top of the
/// engine's built-in set. Other subsystems register their constraints, bind
/// sources, and generators as these so <see cref="FuncTemplateEngineHost"/>
/// can fix the whole component set at construction — templates never
/// contribute components, which is what keeps installed packages data-only.
/// </summary>
/// <param name="InterfaceType">
/// The engine component interface the instance is registered under (e.g.
/// <c>ITemplateConstraintFactory</c>, <c>IBindSymbolSource</c>).
/// </param>
/// <param name="Instance">
/// The component instance. Pre-constructed via DI so it can capture func
/// services (the accessor for the extension-bundle constraint, loggers, …).
/// </param>
internal sealed record FuncEngineComponent(Type InterfaceType, IIdentifiedComponent Instance);
