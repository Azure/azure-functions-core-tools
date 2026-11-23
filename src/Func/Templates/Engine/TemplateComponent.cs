// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Represents a template component that can be registered with the dependency injection container.
/// </summary>
public interface ITemplateComponent
{
    /// <summary>
    /// Gets the declared interface type to register to <see cref="IComponentManager"/> as.
    /// </summary>
    public Type InterfaceType { get; }

    /// <summary>
    /// Gets the identified component instance.
    /// </summary>
    public IIdentifiedComponent Instance { get; }
}
