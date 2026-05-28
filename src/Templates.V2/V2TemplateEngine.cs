// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// Runs a <see cref="NewTemplate"/>'s job and action graph against a
/// variables dictionary built from user-supplied option values + per-input
/// defaults, then materialises files declared by <c>WriteToFile</c> actions.
/// PR2 covers the two action types that drive Node and Python v2 templates
/// (<c>GetTemplateFileContent</c>, <c>WriteToFile</c>); other action types
/// surface as <see cref="TemplateApplicationFailure.ProviderError"/>.
/// </summary>
/// <remarks>
/// Callers supply <paramref name="optionValuesByPromptId"/> — a mapping from
/// each prompt's <see cref="TemplateUserPrompt.Id"/> (= the v2
/// <c>paramId</c>) to the user-supplied value. PR2's provider derives this
/// from a minimal "use defaults" map; PR4 replaces it with the stage-B
/// parse result built by the orchestrator.
/// </remarks>
internal sealed class V2TemplateEngine
{
    private static readonly Regex _substitutionPattern = new(@"\$\(([A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.Compiled);

    public TemplateApplicationResult Apply(
        NewTemplate template,
        string functionName,
        IReadOnlyDictionary<string, string?> optionValuesByPromptId,
        DirectoryInfo workingDirectory,
        bool force)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(optionValuesByPromptId);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FUNCTION_NAME_INPUT"] = functionName,
        };

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
                    if (string.IsNullOrWhiteSpace(input.AssignTo))
                    {
                        continue;
                    }

                    string? varName = ExtractVarName(input.AssignTo);
                    if (varName is null)
                    {
                        continue;
                    }

                    string? value = ResolveInputValue(input, optionValuesByPromptId);
                    if (value is null && input.Required)
                    {
                        return new TemplateApplicationResult.Failed(
                            new TemplateApplicationFailure.InvalidPrompt(
                                input.ParamId ?? input.AssignTo,
                                $"Required input '{input.ParamId ?? input.AssignTo}' has no supplied value or default."));
                    }

                    if (value is not null)
                    {
                        variables[varName] = value;
                    }
                }
            }
        }

        List<string> writtenFiles = [];
        List<string> existingFiles = [];

        if (template.Actions is { Count: > 0 })
        {
            foreach (V2Action action in template.Actions)
            {
                switch ((action.Type ?? string.Empty).ToLowerInvariant())
                {
                    case "gettemplatefilecontent":
                        {
                            string? filePath = Substitute(action.FilePath, variables);
                            if (filePath is null)
                            {
                                continue;
                            }

                            if (template.Files is null || !template.Files.TryGetValue(filePath, out string? content))
                            {
                                return Failed(
                                    $"GetTemplateFileContent: file '{filePath}' not found in template.files map.");
                            }

                            string? assign = ExtractVarName(action.AssignTo);
                            if (assign is not null)
                            {
                                variables[assign] = Substitute(content, variables) ?? content;
                            }
                            break;
                        }

                    case "writetofile":
                        {
                            string? filePath = Substitute(action.FilePath, variables);
                            if (filePath is null)
                            {
                                return Failed("WriteToFile: filePath is required.");
                            }

                            string content = Substitute(action.FileContent, variables) ?? string.Empty;
                            string fullPath = Path.GetFullPath(Path.Combine(workingDirectory.FullName, filePath));

                            if (File.Exists(fullPath) && !force)
                            {
                                existingFiles.Add(fullPath);
                                continue;
                            }

                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                                File.WriteAllText(fullPath, content);
                                writtenFiles.Add(fullPath);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                return new TemplateApplicationResult.Failed(
                                    new TemplateApplicationFailure.WriteFailed(fullPath, ex.Message));
                            }
                            break;
                        }

                    default:
                        if (!action.ContinueOnError)
                        {
                            return Failed($"Unsupported v2 action type '{action.Type}'.");
                        }
                        break;
                }
            }
        }

        if (existingFiles.Count > 0)
        {
            return new TemplateApplicationResult.AlreadyExists(existingFiles);
        }

        return new TemplateApplicationResult.Created(writtenFiles);
    }

    private static string? ResolveInputValue(V2Input input, IReadOnlyDictionary<string, string?> optionValuesByPromptId)
    {
        if (input.ParamId is not null
            && optionValuesByPromptId.TryGetValue(input.ParamId, out string? supplied)
            && !string.IsNullOrEmpty(supplied))
        {
            return supplied;
        }

        return input.DefaultValue;
    }

    private static string? Substitute(string? template, IReadOnlyDictionary<string, string> variables)
    {
        if (template is null)
        {
            return null;
        }

        return _substitutionPattern.Replace(template, match =>
        {
            string name = match.Groups[1].Value;
            return variables.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    private static string? ExtractVarName(string? assignTo)
    {
        if (string.IsNullOrWhiteSpace(assignTo))
        {
            return null;
        }

        Match m = _substitutionPattern.Match(assignTo);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static TemplateApplicationResult.Failed Failed(string message)
        => new(new TemplateApplicationFailure.ProviderError(message, null));
}
