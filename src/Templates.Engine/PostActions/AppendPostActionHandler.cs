// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Runs the func-owned append post action (Python v2 model): it copies the
/// rendered snippet from the provider-owned staging directory into the user's
/// <c>function_app.py</c> or a blueprint file. This is the only handler that
/// writes into the project tree, so a failed append never orphans engine
/// output there — the staged snippet is left for manual recovery.
/// </summary>
internal sealed class AppendPostActionHandler(IFuncTemplateFileSystem fileSystem) : IFuncPostActionHandler
{
    private const string TargetFileParamArg = "targetFileParam";
    private const string AppObjectParamArg = "appObjectParam";
    private const string DeleteStagedFileArg = "deleteStagedFile";

    private readonly IFuncTemplateFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc />
    public Guid ActionId => FuncPostActionIds.Append;

    /// <inheritdoc />
    public Task<FuncPostActionResult> ExecuteAsync(FuncPostActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(context));
    }

    private FuncPostActionResult Execute(FuncPostActionContext context)
    {
        IPostAction action = context.PostAction;
        bool continueOnError = action.ContinueOnError;

        string? targetFileParam = GetArg(action, TargetFileParamArg);
        if (string.IsNullOrEmpty(targetFileParam))
        {
            return new FuncPostActionResult.Failed(
                $"The append post-action is missing its '{TargetFileParamArg}' argument.", continueOnError);
        }

        string? targetFile = GetValue(context.ParameterValues, targetFileParam);
        if (string.IsNullOrWhiteSpace(targetFile))
        {
            return new FuncPostActionResult.Failed(
                $"The append post-action target parameter '{targetFileParam}' has no value.", continueOnError);
        }

        string? appObjectParam = GetArg(action, AppObjectParamArg);
        string appObject = (appObjectParam is null ? null : GetValue(context.ParameterValues, appObjectParam)) ?? "app";

        if (context.CreatedFiles.Count == 0)
        {
            return new FuncPostActionResult.Failed(
                "The append template produced no staged snippet to append.", continueOnError);
        }

        string stagedPath = Path.Combine(context.OutputBasePath, context.CreatedFiles[0]);
        if (!_fileSystem.FileExists(stagedPath))
        {
            return new FuncPostActionResult.Failed(
                $"The staged snippet '{stagedPath}' was not found.", continueOnError);
        }

        string snippet = Normalize(_fileSystem.ReadAllText(stagedPath)).TrimEnd();
        string targetPath = Path.Combine(context.ProjectDirectory, targetFile);
        string? existingContent = _fileSystem.FileExists(targetPath) ? _fileSystem.ReadAllText(targetPath) : null;

        if (existingContent is not null && Normalize(existingContent).Contains(
                $"def {context.FunctionName}(", StringComparison.Ordinal))
        {
            // The duplicate guard is intentionally not overridable by --force:
            // appending a second identically-named function would shadow the first.
            return new FuncPostActionResult.Failed(
                $"A function named '{context.FunctionName}' already exists in '{targetFile}'. Choose a different name.",
                ContinueOnError: false);
        }

        bool isBlueprintCreate = existingContent is null && !string.Equals(appObject, "app", StringComparison.Ordinal);

        try
        {
            if (existingContent is not null)
            {
                // Append only the new block so the user's existing bytes — their
                // byte-order mark and line endings included — stay untouched;
                // match the file's newline style so we never inject a lone LF.
                string newline = existingContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                _fileSystem.AppendAllText(targetPath, newline + newline + snippet.Replace("\n", newline) + newline);
            }
            else
            {
                string header = isBlueprintCreate ? BuildBlueprintHeader(appObject) : BuildAppHeader(appObject);
                _fileSystem.WriteAllText(targetPath, header + "\n\n\n" + snippet + "\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FuncPostActionResult.Failed(
                $"Failed to write '{targetFile}'. The rendered snippet is preserved at '{stagedPath}' for manual recovery.",
                continueOnError,
                ex,
                PreserveStagedContent: true);
        }

        List<string> instructions = [];
        if (isBlueprintCreate)
        {
            string module = Path.GetFileNameWithoutExtension(targetFile);
            instructions.Add($"Register the '{module}' blueprint in your function_app.py:");
            instructions.Add($"    from {module} import {appObject}");
            instructions.Add($"    app.register_functions({appObject})");
        }

        if (string.Equals(GetArg(action, DeleteStagedFileArg), "true", StringComparison.OrdinalIgnoreCase))
        {
            _fileSystem.DeleteFile(stagedPath);
        }

        return new FuncPostActionResult.Succeeded { ModifiedFiles = [targetFile], Instructions = instructions };
    }

    private static string BuildAppHeader(string appObject) =>
        $"import azure.functions as func\nimport logging\n\n{appObject} = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)";

    private static string BuildBlueprintHeader(string appObject) =>
        $"import azure.functions as func\nimport logging\n\n{appObject} = func.Blueprint()";

    private static string Normalize(string content) => content.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string? GetArg(IPostAction action, string key) =>
        action.Args.TryGetValue(key, out string? value) ? value : null;

    private static string? GetValue(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : null;
}
