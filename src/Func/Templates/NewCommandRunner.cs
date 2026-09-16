// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

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
    INewCommandResultRenderer resultRenderer) : INewCommandRunner
{
    private readonly INewCommandContextResolver _contextResolver = contextResolver ?? throw new ArgumentNullException(nameof(contextResolver));
    private readonly INewCommandBundleValidator _bundleValidator = bundleValidator ?? throw new ArgumentNullException(nameof(bundleValidator));
    private readonly INewCommandTemplateCatalog _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    private readonly INewCommandTemplateSelector _templateSelector = templateSelector ?? throw new ArgumentNullException(nameof(templateSelector));
    private readonly INewCommandTemplateApplicator _templateApplicator = templateApplicator ?? throw new ArgumentNullException(nameof(templateApplicator));
    private readonly NewCommandRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly INewCommandResultRenderer _resultRenderer = resultRenderer ?? throw new ArgumentNullException(nameof(resultRenderer));

    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

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
    IReadOnlyDictionary<string, string?>? UserOptionValues = null);
