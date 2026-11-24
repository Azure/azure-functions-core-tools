// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Identifies a template extension supplied by a workload.
/// </summary>
public interface ITemplateComponent
{
    /// <summary>
    /// Gets the identifier used to resolve this component.
    /// </summary>
    public Guid Id { get; }
}
