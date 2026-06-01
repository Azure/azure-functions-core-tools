// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// Projects a v2 <see cref="NewTemplate"/> + its referenced
/// <see cref="UserPromptDoc"/>s into the engine-agnostic
/// <see cref="FunctionTemplateInfo"/> the orchestrator consumes.
/// </summary>
internal static class V2TemplateProjection
{
    public static FunctionTemplateInfo? Project(NewTemplate template, V2Payload payload, string stack)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(template.Id))
        {
            return null;
        }

        // The same prompt id can appear on inputs across multiple jobs
        // (a v2 template typically has CreateNewApp / AppendToFile /
        // CreateNewBlueprint / AppendToBlueprint jobs all asking for the
        // same trigger-functionName). Dedupe on the prompt id so the
        // hydrator surfaces each option once.
        List<TemplateUserPrompt> prompts = [];
        HashSet<string> seenPromptIds = new(StringComparer.OrdinalIgnoreCase);

        if (template.Jobs is { Count: > 0 })
        {
            foreach (V2Job job in template.Jobs)
            {
                if (job.Inputs is null)
                {
                    continue;
                }

                foreach (V2Input input in job.Inputs)
                {
                    TemplateUserPrompt? prompt = ProjectInput(input, payload);
                    if (prompt is null)
                    {
                        continue;
                    }

                    if (!seenPromptIds.Add(prompt.Id))
                    {
                        continue;
                    }

                    prompts.Add(prompt);
                }
            }
        }

        string? description = ResolveResource(template.Description, payload.Resources);
        string displayName = ResolveResource(template.Name, payload.Resources) ?? template.Id;

        IReadOnlyList<string> languages = string.IsNullOrWhiteSpace(template.Language)
            ? []
            : [template.Language];

        TemplateMetadata metadata = new(prompts, RequiresExtensionBundle: true, MinBundleVersion: null);

        return new FunctionTemplateInfo(
            Id: template.Id,
            Stack: stack,
            EngineId: EngineIds.V2,
            DisplayName: displayName,
            Description: description,
            DefaultFunctionName: ResolveDefaultFunctionName(prompts),
            Languages: languages,
            Metadata: metadata);
    }

    // Picks up the per-template default for `func new` without `--name`. The
    // recognised prompt ids mirror V2EngineProvider's function-name prompt set
    // (Node v4 / Python v2 bundle templates both use trigger-functionName).
    private static string? ResolveDefaultFunctionName(IReadOnlyList<TemplateUserPrompt> prompts)
    {
        foreach (TemplateUserPrompt prompt in prompts)
        {
            if (_functionNamePromptIds.Contains(prompt.Id) && !string.IsNullOrWhiteSpace(prompt.DefaultValue))
            {
                return prompt.DefaultValue;
            }
        }

        return null;
    }

    private static readonly HashSet<string> _functionNamePromptIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "functionName",
        "function-name",
        "trigger-functionName",
        "trigger-functionname",
    };

    private static TemplateUserPrompt? ProjectInput(V2Input input, V2Payload payload)
    {
        if (string.IsNullOrWhiteSpace(input.ParamId))
        {
            return null;
        }

        payload.UserPrompts.TryGetValue(input.ParamId, out UserPromptDoc? doc);
        string id = input.ParamId;
        string? description = ResolveResource(doc?.Label, payload.Resources);
        string dataType = doc?.Enum is { Count: > 0 } ? "choice" : "string";
        string? defaultValue = input.DefaultValue ?? doc?.DefaultValue;
        IReadOnlyList<string>? choices = doc?.Enum?
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Value!)
            .ToList();
        bool required = input.Required || (doc?.Required ?? false);
        string? validator = doc?.Validators?.FirstOrDefault()?.Expression;

        return new TemplateUserPrompt(
            Id: id,
            Description: description,
            DataType: dataType,
            DefaultValue: defaultValue,
            Choices: choices,
            IsRequired: required,
            ValidatorRegex: validator,
            ShortAlias: null,
            LongAlias: null);
    }

    private static string? ResolveResource(string? maybeResourceKey, IReadOnlyDictionary<string, string> resources)
    {
        if (string.IsNullOrWhiteSpace(maybeResourceKey))
        {
            return maybeResourceKey;
        }

        if (maybeResourceKey.StartsWith('$') && resources.TryGetValue(maybeResourceKey[1..], out string? resolved))
        {
            return resolved;
        }

        return maybeResourceKey;
    }
}
