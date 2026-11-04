// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Containers;

internal sealed class ContainerArtifactOrchestrator(
    ITemplaterFactory templaterFactory,
    TemplateArgumentParser argumentParser)
{
    private readonly ITemplaterFactory _templaterFactory =
        templaterFactory ?? throw new ArgumentNullException(nameof(templaterFactory));
    private readonly TemplateArgumentParser _argumentParser =
        argumentParser ?? throw new ArgumentNullException(nameof(argumentParser));

    /// <summary>
    /// Resolves and validates template-owned arguments before project initialization.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ParseArgumentsAsync(
        WorkingDirectory projectRoot,
        string stack,
        string? language,
        ContainerArtifactCapability capability,
        IReadOnlyList<string> argumentTokens,
        CancellationToken cancellationToken)
    {
        TemplateEngineProjectContext projectContext = new(projectRoot, stack, language);
        using Templater templater = _templaterFactory.Create(new(projectRoot, projectContext));
        TemplateArgumentParseResult parsed = await ResolveAndParseAsync(
            templater,
            stack,
            language,
            capability,
            argumentTokens,
            cancellationToken);
        return parsed.Values;
    }

    /// <summary>
    /// Selects and invokes the single workload-owned artifact template for a resolved project.
    /// </summary>
    public async Task<TemplateInvocationResult> GenerateAsync(
        FunctionsProject project,
        ContainerArtifactCapability capability,
        IReadOnlyList<string> argumentTokens,
        bool force,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(argumentTokens);

        TemplateEngineProjectContext projectContext;
        try
        {
            projectContext = new(
                project.WorkingDirectory,
                project.StackName,
                project.Language,
                project.TemplateHostParameters);
        }
        catch (InvalidOperationException ex)
        {
            throw new ContainerArtifactGenerationException(ex.Message, ex);
        }
        using Templater templater = _templaterFactory.Create(new(project.WorkingDirectory, projectContext));
        TemplateArgumentParseResult parsed = await ResolveAndParseAsync(
            templater,
            project.StackName,
            project.Language,
            capability,
            argumentTokens,
            cancellationToken);

        try
        {
            return await parsed.Template!.InvokeAsync(
                new(
                    project.WorkingDirectory.Info.FullName,
                    parsed.Values,
                    force,
                    dryRun ? TemplateInvocationMode.DryRun : TemplateInvocationMode.Create),
                cancellationToken);
        }
        catch (TemplateInvocationException ex)
        {
            throw new ContainerArtifactGenerationException(ex.Message, ex);
        }
    }

    private async Task<TemplateArgumentParseResult> ResolveAndParseAsync(
        Templater templater,
        string stack,
        string? language,
        ContainerArtifactCapability capability,
        IReadOnlyList<string> argumentTokens,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ResolvedTemplate> candidates = await templater.ResolveArtifactTemplatesAsync(
            CapabilityName(capability),
            stack,
            language,
            cancellationToken);
        if (candidates.Count == 0)
        {
            throw new ContainerArtifactGenerationException(
                $"The '{stack}' workload does not provide {Display(capability)} container artifact templates.");
        }

        TemplateArgumentParseResult parsed = _argumentParser.Parse(candidates, argumentTokens);
        if (!parsed.IsSuccess)
        {
            throw new ContainerArtifactGenerationException(parsed.Diagnostics[0].Message);
        }

        return parsed;
    }

    private static string CapabilityName(ContainerArtifactCapability capability) => capability switch
    {
        ContainerArtifactCapability.Docker => "docker",
        ContainerArtifactCapability.FlexByoc => "flex-byoc",
        _ => capability.ToString(),
    };

    private static string Display(ContainerArtifactCapability capability) => capability switch
    {
        ContainerArtifactCapability.Docker => "Docker",
        ContainerArtifactCapability.FlexByoc => "Flex BYOC",
        _ => capability.ToString(),
    };
}
