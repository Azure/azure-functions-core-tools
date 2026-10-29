// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Hydrates a single template's options for the help and pre-parse passes in
/// <c>NewCommand</c>.
/// </summary>
/// <remarks>
/// These callers are best-effort and return <c>null</c> silently when
/// resolution fails. The execute and list entry-points resolve and render
/// failures themselves, so rendering here too would surface the same error
/// twice for a single invocation (issue #5304).
/// </remarks>
internal interface INewCommandTemplateOptionProvider
{
    public FunctionTemplateInfo? SelectedPackageTemplate { get; }

    public Task<IReadOnlyList<Option>?> HydrateOptionsForTemplateAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<HydratedTemplateOption>?> HydrateOptionsForTemplateWithIdsAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken);
}

internal sealed class NewCommandTemplateOptionProvider(
    INewCommandContextResolver contextResolver,
    INewCommandTemplateCatalog templateCatalog,
    TemplateOptionHydrator optionHydrator,
    IPublicPackageTemplates packages) : INewCommandTemplateOptionProvider
{
    private readonly INewCommandContextResolver _contextResolver =
        contextResolver ?? throw new ArgumentNullException(nameof(contextResolver));
    private readonly INewCommandTemplateCatalog _templateCatalog =
        templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    private readonly TemplateOptionHydrator _optionHydrator =
        optionHydrator ?? throw new ArgumentNullException(nameof(optionHydrator));
    private readonly IPublicPackageTemplates _packages = packages ?? throw new ArgumentNullException(nameof(packages));

    public FunctionTemplateInfo? SelectedPackageTemplate { get; private set; }

    public async Task<IReadOnlyList<Option>?> HydrateOptionsForTemplateAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HydratedTemplateOption>? paired =
            await HydrateOptionsForTemplateWithIdsAsync(invocation, templateId, cancellationToken);

        return paired?.Select(pair => pair.Option).ToList();
    }

    public async Task<IReadOnlyList<HydratedTemplateOption>?> HydrateOptionsForTemplateWithIdsAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        SelectedPackageTemplate = null;
        if (_packages.IsEnabled && _packages.IsPackageReference(templateId)
            && await _packages.IsNodeJavaScriptAsync(invocation.WorkingDirectory, cancellationToken))
        {
            PublicPackageTemplates.RejectForce(invocation.Force);
            TemplateDescriptor descriptor = await _packages.ReadItemAsync(invocation.WorkingDirectory, cancellationToken);
            SelectedPackageTemplate = _packages.ProjectItem(descriptor.Metadata);
            return [];
        }

        NewCommandResolutionResult outcome = await _contextResolver.ResolveAsync(invocation, cancellationToken);
        if (outcome.Context is not { } resolved)
        {
            return null;
        }

        IReadOnlyList<FunctionTemplateInfo> templates = await _templateCatalog.ListAsync(resolved, cancellationToken);
        FunctionTemplateInfo? template = templates.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, templateId, StringComparison.OrdinalIgnoreCase));

        return template is null ? null : _optionHydrator.HydrateWithIds(template);
    }
}