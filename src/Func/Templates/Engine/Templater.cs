// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
internal sealed class Templater(TemplateEngineSession session) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Enumerates the templates installed in the func hive.
    /// </summary>
    public Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return _session.PackageManager.GetTemplatesAsync(cancellationToken);
    }

    public void Dispose() => _session.Dispose();
}
