// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates the command-scoped <see cref="Templater"/> for a resolved <see cref="TemplateEngineContext"/>.
/// </summary>
internal interface ITemplaterFactory
{
    /// <summary>
    /// Creates a templater bound to <paramref name="context"/>. The caller disposes it when the command is finished.
    /// </summary>
    public Templater Create(TemplateEngineContext context);
}
