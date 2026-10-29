// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandRunner
{
    public Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken);

    public Task<int> ListAsync(NewInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates the <c>func new</c> resolution, selection, validation, and
/// application pipeline through focused template services.
/// </summary>
internal sealed class NewCommandRunner(
    INewCommandContextResolver contextResolver,
    INewCommandBundleValidator bundleValidator,
    INewCommandTemplateCatalog templateCatalog,
    INewCommandTemplateSelector templateSelector,
    INewCommandTemplateApplicator templateApplicator,
    NewCommandRenderer renderer,
    INewCommandResultRenderer resultRenderer,
    IPublicPackageTemplates packages) : INewCommandRunner
{
    private readonly INewCommandContextResolver _contextResolver = contextResolver ?? throw new ArgumentNullException(nameof(contextResolver));
    private readonly INewCommandBundleValidator _bundleValidator = bundleValidator ?? throw new ArgumentNullException(nameof(bundleValidator));
    private readonly INewCommandTemplateCatalog _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    private readonly INewCommandTemplateSelector _templateSelector = templateSelector ?? throw new ArgumentNullException(nameof(templateSelector));
    private readonly INewCommandTemplateApplicator _templateApplicator = templateApplicator ?? throw new ArgumentNullException(nameof(templateApplicator));
    private readonly NewCommandRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly INewCommandResultRenderer _resultRenderer = resultRenderer ?? throw new ArgumentNullException(nameof(resultRenderer));
    private readonly IPublicPackageTemplates _packages = packages ?? throw new ArgumentNullException(nameof(packages));

    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (_packages.IsEnabled && await _packages.IsNodeJavaScriptAsync(invocation.WorkingDirectory, cancellationToken))
        {
            if (_packages.IsPackageReference(invocation.RequestedTemplate))
            {
                return await _packages.CreateAsync(invocation, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(invocation.RequestedTemplate))
            {
                IReadOnlyList<FunctionTemplateInfo>? supported = await ReadSupportedCatalogAsync(invocation, cancellationToken);
                if (supported is null)
                {
                    return 1;
                }

                FunctionTemplateInfo? selected = await _templateSelector.SelectAsync(invocation, supported, cancellationToken);
                if (selected is null)
                {
                    return 1;
                }

                invocation = invocation with { RequestedTemplate = selected.Id };
                if (selected.EngineId == PublicPackageTemplates.EngineId)
                {
                    return await _packages.CreateAsync(invocation, cancellationToken);
                }
            }
        }

        NewCommandResolutionResult outcome = await _contextResolver.ResolveAsync(invocation, cancellationToken);
        if (outcome.Failure is { } executeFailure)
        {
            _resultRenderer.RenderResolutionFailure(executeFailure);
            return 1;
        }

        NewCommandResolvedContext resolved = outcome.Context!;

        if (resolved.UsedStableFallback)
        {
            _renderer.RenderTemplatesChannelFallback(resolved.Stack, resolved.BundleId!, resolved.Channel);
        }

        // Steps 11a / 11b: extension-bundle presence + min-bundle gate.
        // DotNet doesn't ship a templates-workload.json, so the gate is a
        // no-op for it; Node and Python carry one.
        int bundleGate = await _bundleValidator.ValidateAsync(resolved, cancellationToken);
        if (bundleGate != 0)
        {
            return bundleGate;
        }

        // Step 6: aggregate templates from every registered engine for the
        // active stack.
        IReadOnlyList<FunctionTemplateInfo> templates = await _templateCatalog.ListAsync(resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(resolved.Stack);
            return 1;
        }

        // Step 7: resolve --template, falling back to the picker in
        // interactive mode (errors when neither --template nor an
        // interactive shell is available).
        FunctionTemplateInfo? template = await _templateSelector.SelectAsync(
            invocation, templates, cancellationToken);
        if (template is null)
        {
            return 1;
        }

        TemplateApplicationResult applyResult = await _templateApplicator.ApplyAsync(
            invocation,
            resolved,
            template,
            cancellationToken);

        return _resultRenderer.RenderApplyResult(template, applyResult);
    }

    /// <summary>
    /// Lists templates for <c>func new --list</c>. Same resolution
    /// gates as <see cref="ExecuteAsync"/> minus the stage-B / apply tail.
    /// </summary>
    public async Task<int> ListAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (_packages.IsEnabled && await _packages.IsNodeJavaScriptAsync(invocation.WorkingDirectory, cancellationToken))
        {
            PublicPackageTemplates.ValidateOutput(invocation.OutputFormat);
            IReadOnlyList<FunctionTemplateInfo>? supported = await ReadSupportedCatalogAsync(invocation, cancellationToken);
            if (supported is null)
            {
                return 1;
            }

            if (invocation.JsonOutput)
            {
                _renderer.RenderCatalogueJson("node", "javascript", supported);
            }
            else
            {
                _renderer.RenderCatalogue("node", "javascript", supported);
            }

            return 0;
        }

        NewCommandResolutionResult outcome = await _contextResolver.ResolveAsync(invocation, cancellationToken);
        if (outcome.Failure is { } listFailure)
        {
            _resultRenderer.RenderResolutionFailure(listFailure);
            return 1;
        }

        NewCommandResolvedContext resolved = outcome.Context!;

        if (resolved.UsedStableFallback)
        {
            _renderer.RenderTemplatesChannelFallback(resolved.Stack, resolved.BundleId!, resolved.Channel);
        }

        IReadOnlyList<FunctionTemplateInfo> templates = await _templateCatalog.ListAsync(resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(resolved.Stack);
            return 1;
        }

        if (invocation.JsonOutput)
        {
            _renderer.RenderCatalogueJson(resolved.Stack, resolved.Language, templates);
        }
        else
        {
            _renderer.RenderCatalogue(resolved.Stack, resolved.Language, templates);
        }

        return 0;
    }

    private async Task<IReadOnlyList<FunctionTemplateInfo>?> ReadSupportedCatalogAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        TemplateDescriptor descriptor = await _packages.ReadItemAsync(invocation.WorkingDirectory, cancellationToken);
        TemplateCatalogEntry item = descriptor.Metadata;
        FunctionTemplateInfo projected = _packages.ProjectItem(item);
        NewCommandResolutionResult legacy = await _contextResolver.ResolveAsync(invocation, cancellationToken);
        List<FunctionTemplateInfo> catalog = [];
        if (legacy.Context is { } context)
        {
            IReadOnlyList<FunctionTemplateInfo> installed = await _templateCatalog.ListAsync(context, cancellationToken);
            catalog.AddRange(installed.Where(template => !string.Equals(template.Id, projected.Id, StringComparison.OrdinalIgnoreCase)));
        }
        else if (legacy.Failure is not { Kind: NewCommandResolutionFailureKind.NoTemplatesWorkloadInstalled
            or NewCommandResolutionFailureKind.NoTemplatesWorkloadForChannel or NewCommandResolutionFailureKind.HostJsonBundleMissing })
        {
            _resultRenderer.RenderResolutionFailure(legacy.Failure!);
            return null;
        }

        if (!item.IsHidden && await _packages.IsCompatibleAsync(invocation.WorkingDirectory, descriptor, cancellationToken))
        {
            catalog.Add(projected);
        }

        return catalog;
    }

}

/// <summary>
/// Bundled invocation context: only the values the runner needs from the
/// SCL parse, decoupled from <c>NewCommand</c>'s argument graph so tests
/// can construct an invocation directly.
/// </summary>
internal sealed record NewInvocation(
    WorkingDirectory WorkingDirectory,
    string? RequestedTemplate,
    string? RequestedFunctionName,
    bool Force,
    bool NonInteractive,
    bool JsonOutput = false,
    IReadOnlyDictionary<string, string?>? UserOptionValues = null)
{
    public ImmutableArray<string> TemplateArguments { get; init; } = [];

    public ImmutableArray<string> ReservedAliases { get; init; } = [];

    public ImmutableArray<string> RepeatedHostOptions { get; init; } = [];

    public string? OutputFormat { get; init; }
}
