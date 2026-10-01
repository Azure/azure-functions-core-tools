// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Template type an installed template declares in <c>tags.type</c>.
/// </summary>
internal enum TemplateType
{
    /// <summary>
    /// An item template, which <c>func new</c> adds to a project.
    /// </summary>
    Item,

    /// <summary>
    /// A project template, which <c>func init</c> creates a project from.
    /// </summary>
    Project,
}
