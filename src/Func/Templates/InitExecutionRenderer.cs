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
                targetDirectory = result.Context?.CommandDirectory.Info.FullName ?? result.Configuration?.Root,
                projectRoot = result.Context?.Project?.RootDirectory.Info.FullName ?? result.Configuration?.Root,
                stack = result.Context?.Project?.Stack ?? result.Configuration?.Stack,
                language = result.Context?.Project?.Language ?? result.Configuration?.Language,
                bundle = result.Context?.Bundle,
                result.Diagnostics,
                result.Configuration,
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
                invocation = ProjectInvocation(result),
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
        if (result.Configuration is { } configuration)
        {
            if (result.Kind is "adoptionPreviewed" or "repairPreviewed")
            {
                _interaction.WriteSectionHeader($"Preview: existing project {(configuration.Repair ? "repair" : "adoption")} (no files written)");
            }
            else if (configuration.Written)
            {
                _interaction.WriteSuccess($"Existing project {(configuration.Repair ? "repaired" : "adopted")} for {configuration.Stack}/{configuration.Language}.");
            }

            _interaction.WriteLine(line => line.Muted(configuration.Written ? "Updated configuration: " : "Planned configuration: ")
                .Code(configuration.Path).Muted($" ({(configuration.Repair ? "Modify" : "Create")}: {configuration.Stack}/{configuration.Language})"));
        }

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
        else if (result.Kind == "partialPreparation")
        {
            _interaction.WriteError("Project files and CLI configuration were created, but stack preparation did not complete. Existing effects were preserved.");
        }

        if (result.Preparation is { } preparation)
        {
            foreach (NativeProjectPreparationStep operation in preparation.Preview.Operations)
            {
                _interaction.WriteHint($"{operation.Operation}: {(operation.Skipped ? "Skipped"
                    : preparation.Completion is null ? "Planned" : preparation.Completion.Succeeded ? "Completed" : "Not completed")}.");
            }

            foreach (string diagnostic in preparation.Preview.Diagnostics)
            {
                _interaction.WriteHint(diagnostic);
            }

            if (preparation.Completion is { Succeeded: true } completed)
            {
                foreach (string diagnostic in completed.Diagnostics)
                {
                    _interaction.WriteHint(diagnostic);
                }
            }
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
            if (result.PlannedCleanup.Count > 0)
            {
                _interaction.WriteSectionHeader("Planned force cleanup (directories include their contents)");
                foreach (string path in result.PlannedCleanup)
                {
                    _interaction.WriteLine(line => line.Muted("Delete: ").Code(path));
                }
            }

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

        if ((result.Preview?.Effects ?? result.Invocation?.Effects) is { } plannedEffects)
        {
            RenderDependencyEffects("Planned dependency effects", plannedEffects.Plan.Effects);
        }

        if (result.Invocation?.Effects is { } actualEffects)
        {
            RenderDependencyEffects("Completed dependency effects", actualEffects.CompletedEffects);
            RenderDependencyEffects("Failed dependency effects", actualEffects.FailedEffects);
        }

        foreach (string diagnostic in (result.Invocation?.Effects ?? result.Preview?.Effects)?.Diagnostics ?? [])
        {
            _interaction.WriteWarning(diagnostic);
        }
    }

    private void RenderDependencyEffects(string title, IReadOnlyList<TemplateEffect> effects)
    {
        if (effects.Count == 0)
        {
            return;
        }

        _interaction.WriteSectionHeader(title);
        foreach (TemplateEffect effect in effects)
        {
            string operation = effect is TemplatePackageReferenceEffect package
                ? $"{effect.GetType().Name} {package.PackageId} {package.Version}"
                : effect.GetType().Name;
            _interaction.WriteLine(line => line.Muted($"{operation}: ").Code(effect.TargetPath));
            if (effect is TemplateFSharpCompileEffect compile)
            {
                foreach (TemplateGeneratedFSharpFile file in compile.GeneratedFiles)
                {
                    _interaction.WriteLine(line => line.Muted("Include before Program.fs: ").Code(file.Path));
                }
            }
        }
    }

    private static IReadOnlyList<TemplateFileChange> GetCompletedChanges(TemplateInvocationResult? invocation)
    {
        // Pre-create conflicts can carry preview changes without writing them.
        return invocation is { Status: TemplateInvocationStatus.Success or TemplateInvocationStatus.ConfigurationFailure or TemplateInvocationStatus.EffectFailure }
            ? invocation.Changes
            : [];
    }

    private static object? ProjectInvocation(InitExecutionResult result)
    {
        if ((result.Invocation ?? result.Preview) is not { } invocation)
        {
            return null;
        }

        Dictionary<string, object> projected = new()
        {
            ["status"] = invocation.Status.ToString(),
            ["plannedChanges"] = result.Preview?.Changes ?? [],
            ["completedChanges"] = GetCompletedChanges(result.Invocation),
            ["plannedConfigurations"] = result.Preview?.ProjectConfigurations ?? [],
            ["configuredProjects"] = result.Invocation?.ConfiguredProjects ?? [],
        };
        if (result.PlannedCleanup.Count > 0)
        {
            projected["plannedCleanup"] = result.PlannedCleanup;
        }
        if (invocation.Diagnostic is { } diagnostic)
        {
            projected["diagnostic"] = diagnostic;
        }

        if ((result.Invocation?.Effects ?? result.Preview?.Effects) is { } effects)
        {
            projected["effects"] = NewExecutionRenderer.ProjectEffects(effects);
        }

        if (result.Preparation is { } preparation)
        {
            projected["preparation"] = new
            {
                planned = preparation.Preview.Operations.Select(step => new { operation = step.Operation.ToString(), step.Skipped }),
                preparation.Preview.Diagnostics,
                completion = preparation.Completion,
            };
        }

        return projected;
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
