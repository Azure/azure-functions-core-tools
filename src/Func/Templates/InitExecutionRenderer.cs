// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed class InitExecutionRenderer(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public int Render(InitExecutionResult result, bool jsonOutput)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (jsonOutput)
        {
            _interaction.WriteJson(new
            {
                result.Kind,
                result.Succeeded,
                targetDirectory = result.Context?.CommandDirectory.Info.FullName,
                projectRoot = result.Context?.Project?.RootDirectory.Info.FullName,
                stack = result.Context?.Project?.Stack,
                language = result.Context?.Project?.Language,
                bundle = result.Context?.Bundle,
                result.Diagnostics,
                templates = result.Templates.Select(ProjectTemplate),
                unavailableTemplates = result.UnavailableTemplates.Select(ProjectTemplate),
                selectedTemplate = result.SelectedTemplate is { } selected ? ProjectTemplate(selected) : null,
                argumentResults = result.ArgumentResults.Select(parsed => new
                {
                    parsed.TemplateIdentity,
                    parsed.ValuesByCanonicalName,
                    parsed.InvalidInputDiagnostics,
                    parsed.DefinitionDiagnostics,
                    missingRequired = parsed.MissingRequiredParameters.Select(parameter => parameter.LongAlias),
                }),
                invocation = (result.Invocation ?? result.Preview) is { } invocation ? new
                {
                    status = invocation.Status.ToString(),
                    plannedChanges = result.Preview?.Changes ?? [],
                    completedChanges = GetCompletedChanges(result.Invocation),
                    plannedConfigurations = result.Preview?.ProjectConfigurations ?? [],
                    configuredProjects = result.Invocation?.ConfiguredProjects ?? [],
                    invocation.Diagnostic,
                } : null,
            });
        }
        else
        {
            RenderPlain(result);
        }

        return result.Succeeded ? 0 : 1;
    }

    public void RenderUnavailable(IReadOnlyList<TemplateCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _interaction.WriteTable(["UNAVAILABLE PROJECT TEMPLATE", "REASON"], entries.Select(entry => new[]
        {
            entry.Identity,
            entry.ProjectConfigurationDiagnostic ?? entry.MetadataDiagnostic ?? entry.ConstraintEvaluation.Status.ToString(),
        }));
        RenderTemplateDiagnostics(entries);
    }

    private void RenderPlain(InitExecutionResult result)
    {
        if (result.Kind == "previewed")
        {
            _interaction.WriteSectionHeader($"Preview: {result.SelectedTemplate!.Identity} (no files written)");
        }
        else if (result.Kind == "created")
        {
            _interaction.WriteSuccess($"Initialized project template '{result.SelectedTemplate!.Identity}'.");
        }
        else if (result.Kind == "partialInitialization")
        {
            _interaction.WriteError("Project files were generated, but mandatory CLI configuration is incomplete. Generated files and completed configurations were preserved.");
        }

        foreach (string diagnostic in result.Diagnostics)
        {
            if (result.Succeeded)
            {
                _interaction.WriteHint(diagnostic);
            }
            else
            {
                _interaction.WriteError(diagnostic);
            }
        }

        if (result.Templates.Count > 0)
        {
            _interaction.WriteTable(["IDENTITY", "NAME", "DECLARED PROJECTS"], result.Templates.Select(entry => new[]
            {
                entry.Identity,
                entry.Name,
                string.Join(", ", (entry.ProjectConfigurations ?? []).Select(project => $"{project.Path}: {project.Stack}/{project.Language}")),
            }));
            RenderTemplateDiagnostics(result.Templates);
        }

        foreach (TemplateCandidateParseResult parsed in result.ArgumentResults)
        {
            foreach (string diagnostic in parsed.DefinitionDiagnostics.Concat(parsed.InvalidInputDiagnostics.Select(diagnostic => diagnostic.Message)))
            {
                _interaction.WriteError($"{parsed.TemplateIdentity}: {diagnostic}");
            }
        }

        if (result.Preview is { } preview)
        {
            _interaction.WriteSectionHeader("Planned effects");
            foreach (TemplateFileChange change in preview.Changes)
            {
                _interaction.WriteLine(line => line.Muted($"{change.Kind}: ").Code(change.Path));
            }
        }

        if (result.Invocation is { } invocation)
        {
            IReadOnlyList<TemplateFileChange> completed = GetCompletedChanges(invocation);
            if (completed.Count > 0)
            {
                _interaction.WriteSectionHeader("Completed effects");
                foreach (TemplateFileChange change in completed)
                {
                    _interaction.WriteLine(line => line.Muted($"{change.Kind}: ").Code(change.Path));
                }
            }

            foreach (string root in invocation.ConfiguredProjects)
            {
                _interaction.WriteLine(line => line.Muted("Configured project: ").Code(root));
            }
        }
    }

    private static IReadOnlyList<TemplateFileChange> GetCompletedChanges(TemplateInvocationResult? invocation)
    {
        // Pre-create conflicts can carry preview changes without writing them.
        return invocation is { Status: TemplateInvocationStatus.Success or TemplateInvocationStatus.ConfigurationFailure }
            ? invocation.Changes
            : [];
    }

    private void RenderTemplateDiagnostics(IReadOnlyList<TemplateCatalogEntry> entries)
    {
        foreach (TemplateCatalogEntry entry in entries)
        {
            foreach (string diagnostic in new[] { entry.MetadataDiagnostic, entry.ProjectConfigurationDiagnostic }.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                _interaction.WriteError($"{entry.Identity}: {diagnostic}");
            }

            foreach (TemplateConstraintDiagnostic diagnostic in entry.ConstraintEvaluation.Diagnostics)
            {
                _interaction.WriteError($"{entry.Identity}: {diagnostic.Message}");
            }
        }

        foreach (string action in entries.SelectMany(entry => entry.ConstraintEvaluation.Diagnostics).Select(diagnostic => diagnostic.CallToAction)
            .OfType<string>().Where(action => !string.IsNullOrWhiteSpace(action)).Distinct(StringComparer.Ordinal))
        {
            _interaction.WriteHint(action);
        }
    }

    private static object ProjectTemplate(TemplateCatalogEntry entry) => new
    {
        entry.Identity,
        entry.Name,
        type = entry.Type.ToString(),
        entry.ShortNames,
        entry.GroupIdentity,
        entry.Precedence,
        entry.ProjectConfigurations,
        entry.ProjectConfigurationDiagnostic,
        entry.MetadataDiagnostic,
        constraintStatus = entry.ConstraintEvaluation.Status.ToString(),
        constraints = entry.ConstraintEvaluation.Diagnostics.Select(diagnostic => new
        {
            diagnostic.Message,
            diagnostic.CallToAction,
        }),
    };
}
