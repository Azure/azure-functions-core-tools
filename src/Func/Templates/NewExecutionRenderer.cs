// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed class NewExecutionRenderer(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public int Render(NewExecutionResult result, bool jsonOutput)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (jsonOutput)
        {
            _interaction.WriteJson(new
            {
                result.Kind,
                result.Succeeded,
                executionDirectory = result.Context?.CommandDirectory.Info.FullName,
                outputDirectory = result.Context?.CommandDirectory.Info.FullName,
                projectRoot = result.Context?.Project?.RootDirectory.Info.FullName,
                stack = result.Context?.Project?.Stack,
                language = result.Context?.Project?.Language,
                bundle = result.Context?.Bundle,
                result.Diagnostics,
                templates = result.Templates.Select(ProjectTemplate),
                selectedTemplate = result.SelectedTemplate is { } selected ? ProjectTemplate(selected) : null,
                argumentResults = result.ArgumentResults.Select(parsed => new
                {
                    parsed.TemplateIdentity,
                    parsed.ValuesByCanonicalName,
                    parameters = parsed.Parameters.Select(parameter => new
                    {
                        parameter.Definition.CanonicalName,
                        parameter.LongAlias,
                        parameter.ShortAlias,
                        parameter.Definition.DataType,
                        parameter.Definition.Choices,
                    }),
                    parsed.InvalidInputDiagnostics,
                    parsed.DefinitionDiagnostics,
                    missingRequired = parsed.MissingRequiredParameters.Select(parameter => parameter.LongAlias),
                }),
                invocation = result.Invocation is { } invocation ? ProjectInvocation(invocation) : null,
            });
        }
        else
        {
            RenderPlain(result);
        }

        return result.Succeeded ? 0 : 1;
    }

    private void RenderPlain(NewExecutionResult result)
    {
        if (result.Kind == "listed")
        {
            _interaction.WriteSectionHeader("Installed item templates");
            if (result.Templates.Count == 0)
            {
                _interaction.WriteHint("No installed item templates match. Run 'func new --list' without filters to inspect available items.");
            }
        }
        else if (result.Kind == "previewed")
        {
            _interaction.WriteSectionHeader($"Preview: {result.SelectedTemplate!.Identity} (no files written)");
        }
        else if (result.Kind == "created")
        {
            _interaction.WriteSuccess($"Created item template '{result.SelectedTemplate!.Identity}'.");
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
            _interaction.WriteTable(["IDENTITY", "NAME", "LANGUAGE"],
                result.Templates.Select(entry => new[] { entry.Identity, entry.Name, entry.Language ?? string.Empty }));
            foreach (TemplateCatalogEntry entry in result.Templates)
            {
                if (entry.MetadataDiagnostic is { } metadata)
                {
                    _interaction.WriteError($"{entry.Identity}: {metadata}");
                }

                foreach (TemplateConstraintDiagnostic diagnostic in entry.ConstraintEvaluation.Diagnostics)
                {
                    _interaction.WriteError($"{entry.Identity}: {diagnostic.Message} {diagnostic.CallToAction}");
                }
            }
        }

        foreach (TemplateCandidateParseResult parsed in result.ArgumentResults)
        {
            foreach (string diagnostic in parsed.DefinitionDiagnostics)
            {
                _interaction.WriteError($"{parsed.TemplateIdentity}: {diagnostic}");
            }

            foreach (TemplateArgumentDiagnostic diagnostic in parsed.InvalidInputDiagnostics)
            {
                _interaction.WriteError($"{parsed.TemplateIdentity}: {diagnostic.Message}");
            }
        }

        if (result.Invocation is { } invocation)
        {
            foreach (TemplateFileChange change in invocation.Changes)
            {
                _interaction.WriteLine(line => line.Muted($"{change.Kind}: ").Code(change.Path));
            }

            if (invocation.Status == TemplateInvocationStatus.FileConflict)
            {
                _interaction.WriteHint("Re-run with --force to permit conflicting file changes.");
            }

            if (invocation.Effects is { } effects)
            {
                foreach (TemplateEffect effect in effects.Plan.Effects)
                {
                    _interaction.WriteLine(line => line.Muted($"{effect.GetType().Name}: ").Code(effect.TargetPath));
                    if (effect is TemplateFSharpCompileEffect compile)
                    {
                        foreach (TemplateGeneratedFSharpFile file in compile.GeneratedFiles)
                        {
                            _interaction.WriteLine(line => line.Muted("Include before Program.fs: ").Code(file.Path));
                        }
                    }
                }

                foreach (string diagnostic in effects.Diagnostics)
                {
                    _interaction.WriteWarning(diagnostic);
                }
            }
        }
    }

    private static object ProjectInvocation(TemplateInvocationResult invocation)
    {
        if (invocation.Effects is not { } effects)
        {
            return new { status = invocation.Status.ToString(), invocation.Changes, invocation.Diagnostic };
        }

        return new
        {
            status = invocation.Status.ToString(),
            invocation.Changes,
            invocation.Diagnostic,
            effects = ProjectEffects(effects),
        };
    }

    internal static object ProjectEffects(NativeTemplateEffectResult effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return new
        {
            effects.Kind,
            effects.Succeeded,
            planned = effects.Plan.Effects.Select(ProjectEffect),
            completed = effects.CompletedEffects.Select(ProjectEffect),
            failed = effects.FailedEffects.Select(ProjectEffect),
            effects.Diagnostics,
        };
    }

    private static object ProjectEffect(TemplateEffect effect)
    {
        if (effect is TemplateFSharpCompileEffect compile)
        {
            return new
            {
                kind = effect.GetType().Name,
                effect.TargetPath,
                effect.Source,
                compile.ExpectedProjectSha256,
                compile.EvaluatedCompileOrder,
                compile.GeneratedFiles,
                effect.ContinueOnError,
            };
        }

        return new
        {
            kind = effect.GetType().Name,
            effect.TargetPath,
            effect.Source,
            packageId = (effect as TemplatePackageReferenceEffect)?.PackageId,
            packageVersion = (effect as TemplatePackageReferenceEffect)?.Version,
            effect.ContinueOnError,
        };
    }

    private static object ProjectTemplate(TemplateCatalogEntry entry) => new
    {
        entry.Identity,
        entry.Name,
        entry.Description,
        type = entry.Type.ToString(),
        entry.ShortNames,
        entry.GroupIdentity,
        entry.Language,
        entry.Precedence,
        entry.Package,
        entry.MetadataDiagnostic,
        constraintStatus = entry.ConstraintEvaluation.Status.ToString(),
        constraints = entry.ConstraintEvaluation.Diagnostics.Select(diagnostic => new
        {
            diagnostic.Label,
            diagnostic.Type,
            status = diagnostic.Status.ToString(),
            diagnostic.Message,
            diagnostic.CallToAction,
        }),
    };
}
