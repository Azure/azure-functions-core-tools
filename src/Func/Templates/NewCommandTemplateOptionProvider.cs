// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandTemplateOptionProvider
{
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
    TemplateOptionHydrator optionHydrator) : INewCommandTemplateOptionProvider
{
    private readonly INewCommandContextResolver _contextResolver =
        contextResolver ?? throw new ArgumentNullException(nameof(contextResolver));
    private readonly INewCommandTemplateCatalog _templateCatalog =
        templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    private readonly TemplateOptionHydrator _optionHydrator =
        optionHydrator ?? throw new ArgumentNullException(nameof(optionHydrator));

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