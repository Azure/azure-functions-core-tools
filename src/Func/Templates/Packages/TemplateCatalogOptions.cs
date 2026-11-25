// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Configures the independent template package catalog.
/// </summary>
internal sealed class TemplateCatalogOptions
{
    /// <summary>
    /// Gets or sets the install feed when no command source is provided.
    /// </summary>
    public string? Source { get; set; }
}
