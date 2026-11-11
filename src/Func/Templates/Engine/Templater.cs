// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

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
    /// Lists the installed templates of <paramref name="type"/>, including hidden templates and templates whose func
    /// host metadata can't be used.
    /// </summary>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        TemplatePackageManager packageManager = _session.PackageManager;
        IReadOnlyList<ITemplateInfo> templates = await packageManager.GetTemplatesAsync(cancellationToken);
        IReadOnlyList<ITemplatePackage> packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        return TemplateCatalogProjector.Project(templates, packages, type);
    }

    public async Task<IReadOnlyList<ResolvedTemplate>> ResolveArtifactTemplatesAsync(
        string capability,
        string stack,
        string? language,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);

        TemplatePackageManager packageManager = _session.PackageManager;
        IReadOnlyList<ITemplateInfo> templates = await packageManager.GetTemplatesAsync(cancellationToken);
        IReadOnlyList<ITemplatePackage> packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        var packagesByMountPoint = packages
            .GroupBy(package => package.MountPointUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        TemplateCreator creator = new(_session.Settings);

        return [.. templates
            .Where(template => TemplateCatalogProjector.GetTemplateType(template) == TemplateType.Artifact)
            .Select(template => (Template: template, Entry: TemplateCatalogProjector.Project(
                template,
                packagesByMountPoint.GetValueOrDefault(template.MountPointUri))))
            .Where(item => item.Entry.MetadataDiagnostic is null
                && string.Equals(item.Entry.Capability, capability, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Entry.Stack, stack, StringComparison.OrdinalIgnoreCase)
                && (item.Entry.Language is null
                    || string.Equals(item.Entry.Language, language, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Entry.Identity, StringComparer.Ordinal)
            .Select(item => new ResolvedTemplate(item.Entry, item.Template, creator))];
    }

    public void Dispose() => _session.Dispose();
}
