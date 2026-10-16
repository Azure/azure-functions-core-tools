// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Preflights a controlled effect plan and applies it only after explicit confirmation, retaining partial outcomes.
/// </summary>
internal sealed class NativeTemplateEffectWorkflow
{
    public async Task<NativeTemplateEffectResult> ExecuteAsync(NativeTemplateEffectPlan plan,
        INativeTemplateEffectCapability? capability, bool dryRun, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (capability is null)
        {
            return new("capabilityRequired", false, plan, [], [], ["No controlled effect capability was supplied; nothing was changed."]);
        }

        NativeTemplateEffectPlanningResult structural = new NativeTemplateEffectPlanner()
            .PlanFilesAndDependencies(plan.OutputDirectory, plan.Effects);
        if (structural.Plan is null)
        {
            return new("invalidPlan", false, plan, [], [], structural.Diagnostics);
        }

        IReadOnlyList<string> preflight = await capability.ValidateAsync(plan, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preflight.Count > 0)
        {
            return new("preflightFailed", false, plan, [], [], preflight);
        }

        if (dryRun)
        {
            return new("previewed", true, plan, [], [], []);
        }

        if (!confirmed)
        {
            return new("declined", false, plan, [], [], ["The effect plan was not confirmed; nothing was changed."]);
        }

        List<TemplateEffect> completed = [];
        List<TemplateEffect> failed = [];
        List<string> diagnostics = [];
        foreach (TemplateEffect effect in plan.Effects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (effect is TemplateEditorNoOpEffect)
            {
                completed.Add(effect);
                continue;
            }

            TemplateEffectExecutionResult result = await capability.ExecuteAsync(effect, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.AddRange(result.Diagnostics);
            if (result.Succeeded)
            {
                completed.Add(effect);
            }
            else
            {
                failed.Add(effect);
                diagnostics.Add($"Effect '{effect.GetType().Name}' failed for '{effect.TargetPath}'."
                    + (result.TargetMayHaveChanged ? " The target may have partial changes; no rollback was attempted." : string.Empty));
                if (!effect.ContinueOnError)
                {
                    break;
                }
            }
        }

        return new(failed.Count == 0 ? "applied" : "partialFailure", failed.Count == 0,
            plan, completed.AsReadOnly(), failed.AsReadOnly(), diagnostics.AsReadOnly());
    }
}
