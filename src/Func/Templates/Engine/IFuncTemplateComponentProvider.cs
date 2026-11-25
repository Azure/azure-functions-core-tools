// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Builds the components exposed to the template engine.
/// </summary>
internal interface IFuncTemplateComponentProvider
{
    /// <summary>
    /// Gets built-in engine components, core CLI components, and supported workload bridges.
    /// </summary>
    /// <returns>The components to register with the template engine host.</returns>
    public IReadOnlyList<ICoreTemplateComponent> Get();
}
