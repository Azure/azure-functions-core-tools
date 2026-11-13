// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Holds the template candidates discovered for a command.
/// </summary>
internal sealed class TemplateResolutionResult(IReadOnlyList<FuncTemplateInfo> templates)
{
    public IReadOnlyList<FuncTemplateInfo> Templates { get; } = templates ?? throw new ArgumentNullException(nameof(templates));
}
