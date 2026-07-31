// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads item templates from the engine cache and projects them to the CLI's
/// <see cref="FunctionTemplateInfo"/> shape, filtered by the stack/language in
/// <see cref="TemplateListContext"/>. All reads are served from the cache with
/// no network I/O. <see cref="ListAsync"/> returns the selectable set with
/// constraint-restricted templates excluded; <see cref="FindRestrictedAsync"/>
/// surfaces a restricted template so an explicit request can explain why it is
/// unavailable.
/// </summary>
internal interface IFuncTemplateCatalog
{
    /// <summary>
    /// Lists the templates available for the context's stack and language.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public Task<IReadOnlyList<FunctionTemplateInfo>> ListAsync(TemplateListContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a constraint-restricted template matching <paramref name="requestedTemplate"/>
    /// (by id or short name) for the context's stack and language, or <c>null</c>
    /// when no restricted template matches. Lets an explicit request surface the
    /// restriction reason and call-to-action instead of a bare "unknown template".
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestedTemplate"/> is null or whitespace.</exception>
    public Task<RestrictedTemplateInfo?> FindRestrictedAsync(TemplateListContext context, string requestedTemplate, CancellationToken cancellationToken);
}
