// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Reads root-project markers without creating or clearing directories.
/// </summary>
internal class InitExecutionTargetInspector(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public virtual async Task<InitExecutionTarget> InspectAsync(WorkingDirectory target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string? diagnostic = GetDiagnostic(target);
        if (diagnostic is null)
        {
            return new(InitExecutionTargetState.Empty);
        }

        string config = CliConfigurationPathsOptions.GetProjectConfigPath(target.Info);
        if (_fileSystem.FileExists(config))
        {
            string existing = await _fileSystem.ReadAllTextIfExistsAsync(config, cancellationToken)
                ?? throw new FileNotFoundException("The project configuration to inspect is missing.", config);
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(existing);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Project configuration must contain a JSON object.");
            }

            string? runtime = null;
            string? language = null;
            if (document.RootElement.TryGetProperty(CliConfigurationNames.StackSectionName, out JsonElement stack))
            {
                if (stack.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Project configuration must contain a stack object.");
                }

                runtime = ReadString(stack, CliConfigurationNames.StackRuntimeKey);
                language = ReadString(stack, CliConfigurationNames.StackLanguageKey);
            }

            return new(InitExecutionTargetState.Configured, runtime, language, ConfigurationJson: existing);
        }

        return _fileSystem.FileExists(Path.Combine(target.Info.FullName, "host.json"))
            ? new(InitExecutionTargetState.Adoptable)
            : new(InitExecutionTargetState.Unsupported, Diagnostic: diagnostic);
    }

    public virtual string? GetDiagnostic(WorkingDirectory target)
    {
        string? pathDiagnostic = GetPathDiagnostic(target);
        return pathDiagnostic ?? (_fileSystem.DirectoryExists(target.Info.FullName) && _fileSystem.GetFileSystemEntries(target.Info.FullName).Count > 0
            ? "The target is not empty and is not an adoptable root Functions project. Supply a root with host.json or use a new empty target; nothing was deleted."
            : null);
    }

    public virtual string? GetPathDiagnostic(WorkingDirectory target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (DirectoryInfo? directory = target.Info; directory is not null; directory = directory.Parent)
        {
            if (_fileSystem.FileExists(directory.FullName))
            {
                return $"'{directory.FullName}' is a file. Supply an empty project directory.";
            }
        }

        return null;
    }

    public virtual bool HasNonGitContent(WorkingDirectory target) =>
        _fileSystem.DirectoryExists(target.Info.FullName) && DirectoryGuard.HasNonGitContent(target.Info);

    public virtual void ClearExceptGit(WorkingDirectory target)
    {
        if (_fileSystem.DirectoryExists(target.Info.FullName))
        {
            DirectoryGuard.ClearExceptGit(target.Info);
        }
    }

    private static string? ReadString(JsonElement stack, string key)
    {
        if (!stack.TryGetProperty(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Project configuration stack.{key} must be a string.");
        }

        return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
    }
}

internal enum InitExecutionTargetState
{
    Empty,
    Adoptable,
    Configured,
    Unsupported,
}

internal sealed record InitExecutionTarget(
    InitExecutionTargetState State, string? Runtime = null, string? Language = null, string? Diagnostic = null, string? ConfigurationJson = null);
