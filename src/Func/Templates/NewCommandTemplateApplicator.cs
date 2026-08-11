// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandTemplateApplicator
{
    public Task<TemplateApplicationResult> ApplyAsync(
        NewInvocation invocation,
        NewCommandResolvedContext context,
        FunctionTemplateInfo template,
        CancellationToken cancellationToken);
}

internal sealed class NewCommandTemplateApplicator(
    ITemplateEngineProviderRegistry engineProviders,
    TemplateOptionHydrator optionHydrator) : INewCommandTemplateApplicator
{
    private readonly ITemplateEngineProviderRegistry _engineProviders =
        engineProviders ?? throw new ArgumentNullException(nameof(engineProviders));
    private readonly TemplateOptionHydrator _optionHydrator =
        optionHydrator ?? throw new ArgumentNullException(nameof(optionHydrator));

    public async Task<TemplateApplicationResult> ApplyAsync(
        NewInvocation invocation,
        NewCommandResolvedContext context,
        FunctionTemplateInfo template,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(template);

        ITemplateEngineProvider? provider = _engineProviders.TryGet(template.EngineId);
        if (provider is null)
        {
            return new TemplateApplicationResult.Failed(
                new TemplateApplicationFailure.ProviderError(
                    $"No engine registered for EngineId '{template.EngineId}'. This is a CLI bug.",
                    InnerException: null));
        }

        _ = _optionHydrator.Hydrate(template);

        string functionName = invocation.RequestedFunctionName
            ?? template.DefaultFunctionName
            ?? template.Id;
        var newContext = new NewContext(
            invocation.WorkingDirectory,
            template,
            functionName,
            context.Language,
            invocation.Force,
            context.Workload.InstallDirectory,
            invocation.UserOptionValues);

        ParseResult emptyParseResult = new RootCommand().Parse(string.Empty);
        return await provider.ApplyAsync(newContext, emptyParseResult, cancellationToken);
    }
}