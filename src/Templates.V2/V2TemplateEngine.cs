// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// Runs a <see cref="NewTemplate"/>'s job and action graph against a
/// variables dictionary built from user-supplied option values + per-input
/// defaults, then materialises files declared by the action sequence.
/// </summary>
/// <remarks>
/// <para>
/// V2 templates expose multiple jobs (e.g. <c>CreateNewApp</c>,
/// <c>AppendToFile</c>, <c>CreateNewBlueprint</c>) that share a single
/// top-level <c>actions</c> pool. Each job declares an <c>actions</c>
/// list naming the actions it runs, in order; this engine selects the
/// first job by default (matching legacy <c>func new</c> behaviour) and
/// runs only that job's named actions rather than every action at the
/// template root.
/// </para>
/// <para>
/// Callers supply <paramref name="optionValuesByPromptId"/> — a mapping
/// from each prompt's <see cref="TemplateUserPrompt.Id"/> (= the v2
/// <c>paramId</c>) to the user-supplied value.
/// </para>
/// <para>
/// Supported action types:
/// <list type="bullet">
/// <item><c>GetTemplateFileContent</c> — load an inline file into a variable.</item>
/// <item><c>WriteToFile</c> — substitute + write a new file (honours
/// <c>--force</c> via the engine call).</item>
/// <item><c>AppendToFile</c> — substitute + append a snippet to an
/// existing file (or create it when <c>createIfNotExists</c> is true).</item>
/// <item><c>ShowMarkdownPreview</c> — no-op (IDE-only hint; not a
/// scaffold step).</item>
/// </list>
/// Other types fall through to a <see cref="TemplateApplicationFailure.ProviderError"/>
/// unless the action declares <c>continueOnError: true</c>.
/// </para>
/// </remarks>
internal sealed class V2TemplateEngine
{
    internal const string FunctionNameVariable = "FUNCTION_NAME_INPUT";

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

        V2Job? job = PickJob(template, workingDirectory);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FunctionNameVariable] = SanitizeFunctionIdentifier(functionName),
        };

        if (job?.Inputs is { Count: > 0 })
        {
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
                    // The function name token is rendered as a Python `def`
                    // or JS/TS function identifier in generated source, so
                    // sanitize it here too. Without this, a name like
                    // "HttpTrigger-Python" passed through the prompt
                    // pipeline would emit `def HttpTrigger-Python(...)` and
                    // fail at parse time with SyntaxError.
                    variables[varName] = varName == FunctionNameVariable
                        ? SanitizeFunctionIdentifier(value)
                        : value;
                }
            }
        }

        // Resolve the action sequence the selected job runs. When the job
        // declares no actions list, fall back to every top-level action in
        // order — preserves behaviour for older / simpler templates that
        // didn't split actions across multiple jobs.
        IReadOnlyList<V2Action> sequence = ResolveActionSequence(template, job);

        List<string> writtenFiles = [];
        List<string> existingFiles = [];

        foreach (V2Action action in sequence)
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
                            if (action.ContinueOnError)
                            {
                                continue;
                            }

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

                        string content = ResolveActionPayload(action, variables);
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

                case "appendtofile":
                    {
                        string? filePath = Substitute(action.FilePath, variables);
                        if (filePath is null)
                        {
                            return Failed("AppendToFile: filePath is required.");
                        }

                        string snippet = ResolveActionPayload(action, variables);
                        string fullPath = Path.GetFullPath(Path.Combine(workingDirectory.FullName, filePath));

                        if (!File.Exists(fullPath))
                        {
                            // Default semantics in the v2 schema: when the
                            // target file is missing and the action doesn't
                            // explicitly opt-in to creation, treat the
                            // append as a no-op (mirrors the upstream
                            // engine's "skip if target absent" behaviour).
                            // CreateIfNotExists=true falls through to a
                            // write of the snippet as the initial content.
                            if (action.CreateIfNotExists != true)
                            {
                                if (action.ContinueOnError)
                                {
                                    continue;
                                }

                                return Failed(
                                    $"AppendToFile: target file '{filePath}' does not exist and createIfNotExists is false.");
                            }

                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                                File.WriteAllText(fullPath, snippet);
                                writtenFiles.Add(fullPath);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                return new TemplateApplicationResult.Failed(
                                    new TemplateApplicationFailure.WriteFailed(fullPath, ex.Message));
                            }
                            break;
                        }

                        try
                        {
                            // Function-body snippets carry no leading or trailing
                            // newline, so guard against the previous append's
                            // closing token running into the next decorator.
                            EnsureTrailingNewline(fullPath);
                            File.AppendAllText(fullPath, snippet);
                            if (!writtenFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                            {
                                writtenFiles.Add(fullPath);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            return new TemplateApplicationResult.Failed(
                                new TemplateApplicationFailure.WriteFailed(fullPath, ex.Message));
                        }
                        break;
                    }

                case "showmarkdownpreview":
                    // Editor-only hint (VS Code surface): the upstream
                    // schema uses this to open a help file alongside the
                    // scaffolded function. No-op for the CLI.
                    break;

                default:
                    if (!action.ContinueOnError)
                    {
                        return Failed($"Unsupported v2 action type '{action.Type}'.");
                    }
                    break;
            }
        }

        if (existingFiles.Count > 0 && writtenFiles.Count == 0)
        {
            return new TemplateApplicationResult.AlreadyExists(existingFiles);
        }

        return new TemplateApplicationResult.Created(writtenFiles);
    }

    private static IReadOnlyList<V2Action> ResolveActionSequence(NewTemplate template, V2Job? job)
    {
        IReadOnlyList<V2Action> allActions = template.Actions ?? (IReadOnlyList<V2Action>)[];

        // No job-level action list → run every top-level action (legacy
        // single-job templates).
        if (job?.Actions is null || job.Actions.Count == 0)
        {
            return allActions;
        }

        // Build a lookup so the resolution is O(n + m); the schema requires
        // action names to be unique within a template, but the lookup is
        // tolerant of case differences seen in real workloads (e.g. job
        // references "showMarkdownPreview" and the action declares
        // "ShowMarkdownPreview").
        var byName = new Dictionary<string, V2Action>(StringComparer.OrdinalIgnoreCase);
        foreach (V2Action action in allActions)
        {
            if (!string.IsNullOrWhiteSpace(action.Name))
            {
                byName[action.Name] = action;
            }
        }

        var sequence = new List<V2Action>(job.Actions.Count);
        foreach (string name in job.Actions)
        {
            if (byName.TryGetValue(name, out V2Action? action))
            {
                sequence.Add(action);
            }
        }

        return sequence;
    }

    private static string ResolveActionPayload(V2Action action, IReadOnlyDictionary<string, string> variables)
    {
        // Real-world v2 actions write content via either `Source` (a
        // substitution expression pointing at a variable, e.g.
        // "$(FUNCTION_BODY)") or `FileContent` (an inline literal). Try
        // Source first; fall back to FileContent so older templates keep
        // working.
        string? sourceSubstituted = Substitute(action.Source, variables);
        if (!string.IsNullOrEmpty(sourceSubstituted))
        {
            return sourceSubstituted;
        }

        return Substitute(action.FileContent, variables) ?? string.Empty;
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

    internal static string? ExtractVarName(string? assignTo)
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

    // Multi-job templates expose CreateNewApp and AppendToFile as alternatives
    // for the same target. Pick the append job when the create-target already
    // exists, so `func new` after `func init` appends instead of failing with
    // AlreadyExists (#5209).
    private static V2Job? PickJob(NewTemplate template, DirectoryInfo workingDirectory)
    {
        if (template.Jobs is not { Count: > 0 })
        {
            return null;
        }

        if (template.Jobs.Count == 1)
        {
            return template.Jobs[0];
        }

        V2Job? createJob = template.Jobs.FirstOrDefault(j =>
            string.Equals(j.Type, "CreateNewApp", StringComparison.OrdinalIgnoreCase));
        V2Job? appendJob = template.Jobs.FirstOrDefault(j =>
            string.Equals(j.Type, "AppendToFile", StringComparison.OrdinalIgnoreCase));

        if (createJob is null)
        {
            return template.Jobs[0];
        }

        if (appendJob is null)
        {
            return createJob;
        }

        return CreateTargetExists(template, createJob, workingDirectory) ? appendJob : createJob;
    }

    // Resolves against input defaults rather than user-supplied values: the
    // relevant filename prompts (app-fileName, app-selectedFileName) are gated
    // to VS Code clients, so the CLI always sees the schema default here.
    private static bool CreateTargetExists(NewTemplate template, V2Job createJob, DirectoryInfo workingDirectory)
    {
        IReadOnlyList<V2Action> sequence = ResolveActionSequence(template, createJob);
        V2Action? writeAction = sequence.FirstOrDefault(a =>
            string.Equals(a.Type, "WriteToFile", StringComparison.OrdinalIgnoreCase));
        if (writeAction?.FilePath is null)
        {
            return false;
        }

        Dictionary<string, string> defaults = BuildInputDefaults(createJob);
        string? resolved = Substitute(writeAction.FilePath, defaults);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(Path.Combine(workingDirectory.FullName, resolved));
        return File.Exists(fullPath);
    }

    private static Dictionary<string, string> BuildInputDefaults(V2Job job)
    {
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (job.Inputs is null)
        {
            return vars;
        }

        foreach (V2Input input in job.Inputs)
        {
            string? varName = ExtractVarName(input.AssignTo);
            if (varName is not null && input.DefaultValue is not null)
            {
                vars[varName] = input.DefaultValue;
            }
        }

        return vars;
    }

    private static void EnsureTrailingNewline(string fullPath)
    {
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length == 0)
        {
            return;
        }

        stream.Seek(-1, SeekOrigin.End);
        int lastByte = stream.ReadByte();
        if (lastByte == '\n')
        {
            return;
        }

        stream.Seek(0, SeekOrigin.End);
        byte[] newline = System.Text.Encoding.UTF8.GetBytes(Environment.NewLine);
        stream.Write(newline, 0, newline.Length);
    }

    /// <summary>
    /// Sanitizes a user-supplied function name so it is safe to emit as a Python
    /// or JavaScript/TypeScript identifier in generated template code. Replaces
    /// any character outside <c>[A-Za-z0-9_]</c> with <c>_</c> and prefixes a
    /// leading <c>_</c> when the first character is a digit.
    /// </summary>
    internal static string SanitizeFunctionIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sanitized = new System.Text.StringBuilder(name.Length + 1);
        foreach (char c in name)
        {
            bool isAsciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            bool isAsciiDigit = c >= '0' && c <= '9';
            sanitized.Append(isAsciiLetter || isAsciiDigit || c == '_' ? c : '_');
        }

        if (sanitized[0] >= '0' && sanitized[0] <= '9')
        {
            sanitized.Insert(0, '_');
        }

        return sanitized.ToString();
    }
}
