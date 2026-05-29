// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Projects each template's <see cref="TemplateUserPrompt"/> collection into
/// a list of <see cref="Option"/> instances for the stage-B parse of
/// <c>func new --template &lt;id&gt;</c>.
///
/// Engine-agnostic: V2 and DotNet engines have already projected their native
/// prompt schemas into <see cref="TemplateUserPrompt"/> before reaching this
/// hydrator, so the same code paths cover both.
/// </summary>
/// <remarks>
/// The hydrator looks up the active stack's
/// <see cref="IProjectInitializer.DefaultFunctionNameValidator"/> via DI when
/// a template's function-name prompt declares no <see cref="TemplateUserPrompt.ValidatorRegex"/>.
/// Template metadata is authoritative; the stack default applies only when
/// the template is silent.
/// </remarks>
internal sealed class TemplateOptionHydrator
{
    /// <summary>
    /// The conventional id workloads use for the function-name prompt — V2
    /// uses <c>trigger-functionName</c> in v4-derived Node templates and the
    /// Python v2 bundle uses similar; DotNet uses <c>name</c>. The hydrator
    /// treats any of these as "the function-name prompt" for the purpose of
    /// applying the per-stack default validator.
    /// </summary>
    private static readonly HashSet<string> _functionNamePromptIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "functionName",
        "function-name",
        "trigger-functionName",
        "trigger-functionname",
    };

    private readonly IReadOnlyDictionary<string, IProjectInitializer> _projectInitializersByStack;

    public TemplateOptionHydrator(IEnumerable<IProjectInitializer> projectInitializers)
    {
        ArgumentNullException.ThrowIfNull(projectInitializers);

        _projectInitializersByStack = projectInitializers
            .Where(p => !string.IsNullOrWhiteSpace(p.Stack))
            .GroupBy(p => p.Stack.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Produces one <see cref="Option"/> per prompt in
    /// <paramref name="template"/>.<see cref="FunctionTemplateInfo.Metadata"/>.<see cref="TemplateMetadata.UserPrompts"/>.
    /// The returned options are <strong>not</strong> attached to a command;
    /// callers wire them into the stage-B parser.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public IReadOnlyList<Option> Hydrate(FunctionTemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<Option> result = [];
        foreach (TemplateUserPrompt prompt in template.Metadata.UserPrompts)
        {
            Option option = BuildOption(template, prompt);
            result.Add(option);
        }

        return result;
    }

    /// <summary>
    /// Same as <see cref="Hydrate"/> but pairs each <see cref="Option"/>
    /// with the originating prompt id. NewCommand uses this overload on
    /// the execute path to map user-supplied values back to the
    /// v2 paramId — <c>Option.Name</c> by then has been kebab-cased and
    /// isn't reversible to the original id.
    /// </summary>
    public IReadOnlyList<HydratedTemplateOption> HydrateWithIds(FunctionTemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<HydratedTemplateOption> result = [];
        foreach (TemplateUserPrompt prompt in template.Metadata.UserPrompts)
        {
            Option option = BuildOption(template, prompt);
            result.Add(new HydratedTemplateOption(option, prompt.Id));
        }

        return result;
    }

    private Option BuildOption(FunctionTemplateInfo template, TemplateUserPrompt prompt)
    {
        string longName = prompt.LongAlias ?? "--" + KebabCase(prompt.Id);
        Option option = prompt.DataType.ToLowerInvariant() switch
        {
            "bool" => BuildBoolOption(prompt, longName),
            "int" => BuildIntOption(prompt, longName),
            _ => BuildStringOption(template, prompt, longName),
        };

        if (!string.IsNullOrWhiteSpace(prompt.Description))
        {
            option.Description = prompt.Description;
        }

        if (!string.IsNullOrWhiteSpace(prompt.ShortAlias))
        {
            option.Aliases.Add(prompt.ShortAlias);
        }

        return option;
    }

    private Option BuildStringOption(FunctionTemplateInfo template, TemplateUserPrompt prompt, string longName)
    {
        var option = new Option<string?>(longName);

        if (prompt.Choices is { Count: > 0 })
        {
            option.AcceptOnlyFromAmong([.. prompt.Choices]);
        }

        if (prompt.DefaultValue is not null)
        {
            string captured = prompt.DefaultValue;
            option.DefaultValueFactory = _ => captured;
        }
        else if (prompt.IsRequired)
        {
            option.Required = true;
        }

        Regex? validator = ResolveValidator(template, prompt);
        if (validator is not null)
        {
            string promptId = prompt.Id;
            option.Validators.Add(result =>
            {
                string? value = result.GetValueOrDefault<string?>();
                if (value is not null && !validator.IsMatch(value))
                {
                    result.AddError(BuildValidatorErrorMessage(promptId, value, validator));
                }
            });
        }

        return option;
    }

    private static Option<bool> BuildBoolOption(TemplateUserPrompt prompt, string longName)
    {
        var option = new Option<bool>(longName);

        if (bool.TryParse(prompt.DefaultValue, out bool defaultBool))
        {
            option.DefaultValueFactory = _ => defaultBool;
        }

        return option;
    }

    private static Option<int?> BuildIntOption(TemplateUserPrompt prompt, string longName)
    {
        var option = new Option<int?>(longName);

        if (int.TryParse(prompt.DefaultValue, out int defaultInt))
        {
            option.DefaultValueFactory = _ => defaultInt;
        }
        else if (prompt.IsRequired)
        {
            option.Required = true;
        }

        return option;
    }

    private Regex? ResolveValidator(FunctionTemplateInfo template, TemplateUserPrompt prompt)
    {
        // Layer 1: template-metadata validator is authoritative.
        if (!string.IsNullOrWhiteSpace(prompt.ValidatorRegex))
        {
            try
            {
                return new Regex(prompt.ValidatorRegex, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                // Malformed regex — leave the option unvalidated rather than
                // crashing the parser. A future enhancement could surface
                // this as InvalidPrompt at hydration time.
                return null;
            }
        }

        // Layer 2: per-stack default fallback, applied only to function-name
        // prompts. Other prompts without a validator are treated as accept-any.
        if (!_functionNamePromptIds.Contains(prompt.Id))
        {
            return null;
        }

        if (_projectInitializersByStack.TryGetValue(template.Stack, out IProjectInitializer? initializer))
        {
            return initializer.DefaultFunctionNameValidator;
        }

        return null;
    }

    private static string BuildValidatorErrorMessage(string promptId, string value, Regex pattern)
    {
        var sb = new StringBuilder();
        sb.Append("Value '").Append(value).Append("' for '").Append(promptId)
          .Append("' does not match the expected pattern (").Append(pattern).Append(").");
        return sb.ToString();
    }

    private static string KebabCase(string id)
    {
        // Convert camelCase / PascalCase to kebab-case:
        //   authLevel    → auth-level
        //   AccessRights → access-rights
        //   HTTPTrigger  → http-trigger   (acronym run, then lower-case)
        //   HTTP         → http           (pure acronym, no insertion)
        // Already-kebab ids pass through unchanged.
        if (string.IsNullOrEmpty(id))
        {
            return id;
        }

        var sb = new StringBuilder(id.Length + 4);
        for (int i = 0; i < id.Length; i++)
        {
            char ch = id[i];

            if (i > 0 && char.IsUpper(ch))
            {
                bool prevIsLower = !char.IsUpper(id[i - 1]);
                bool nextIsLower = i + 1 < id.Length && char.IsLower(id[i + 1]);

                // Insert a separator either when transitioning out of a
                // lower-case run (authLevel → auth-Level) OR when the
                // current capital ends an acronym run that's followed by
                // a lower-case letter (HTTPTrigger → HTTP-Trigger).
                if (prevIsLower || nextIsLower)
                {
                    sb.Append('-');
                }
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }
}

/// <summary>
/// A hydrated CLI option paired with the v2 paramId it was projected
/// from. Used by <c>NewCommand</c> to map user-supplied values on the
/// execute path back to the engine's variable namespace.
/// </summary>
internal sealed record HydratedTemplateOption(Option Option, string PromptId);
