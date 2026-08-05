// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandTemplateCatalog
{
    public Task<IReadOnlyList<FunctionTemplateInfo>> ListAsync(
        NewCommandResolvedContext context,
        CancellationToken cancellationToken);
}

internal sealed class NewCommandTemplateCatalog(ITemplateEngineProviderRegistry engineProviders) : INewCommandTemplateCatalog
{
    private readonly ITemplateEngineProviderRegistry _engineProviders =
        engineProviders ?? throw new ArgumentNullException(nameof(engineProviders));

    public async Task<IReadOnlyList<FunctionTemplateInfo>> ListAsync(
        NewCommandResolvedContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var listContext = new TemplateListContext(
            context.WorkingDirectory,
            context.Stack,
            context.Language,
            context.Workload.InstallDirectory);
        List<FunctionTemplateInfo> templates = [];
        foreach (ITemplateEngineProvider provider in _engineProviders.Providers)
        {
            IReadOnlyList<FunctionTemplateInfo> contributed = await provider.ListTemplatesAsync(listContext, cancellationToken);
            templates.AddRange(contributed);
        }

        return templates;
    }
}