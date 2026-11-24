// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Configuration;

internal sealed class ProjectStackConfiguration(IFileSystem fileSystem, ILocalSettingsProvider localSettings) : IProjectStackConfiguration
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private static readonly JsonNodeOptions _nodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions _documentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ILocalSettingsProvider _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));

    public ProjectStackSettings Read(DirectoryInfo projectDirectory)
    {
        JsonObject? root = ReadRoot(projectDirectory);
        if (root is null)
        {
            LocalSettingsSnapshot local = _localSettings.Get(projectDirectory);
            if (local.WorkerRuntimeReadError is { } error)
            {
                throw new ProjectStackConfigurationException(error);
            }
            return new(WorkerRuntime: local.WorkerRuntime,
                IsAdoptable: _fileSystem.FileExists(Path.Combine(projectDirectory.FullName, "host.json")));
        }

        JsonObject? stack = GetStack(root);
        return new(HasProjectConfig: true,
            StackIdentifier: GetString(stack, CliConfigurationNames.StackRuntimeKey),
            Language: GetString(stack, CliConfigurationNames.StackLanguageKey));
    }

    public async Task CreateAsync(DirectoryInfo projectDirectory, IProjectStack stack, LanguageDescriptor? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stack);
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory);
        JsonObject section = new() { [CliConfigurationNames.StackRuntimeKey] = stack.Descriptor.Identifier };
        if (language is not null)
        {
            LanguageDescriptor supported = GetDeclaredLanguage(stack, language);
            if (stack.SupportedLanguages.Count > 1)
            {
                section[CliConfigurationNames.StackLanguageKey] = supported.LanguageIdentifier;
            }
        }

        JsonObject root = new() { [CliConfigurationNames.StackSectionName] = section };
        // Never replace a config created by another invocation after our initial read.
        string staging = $"{path}.{Guid.NewGuid():N}";
        try
        {
            await _fileSystem.WriteAllTextAsync(staging, root.ToJsonString(_jsonOptions) + Environment.NewLine, cancellationToken);
            _fileSystem.MoveFile(staging, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectStackConfigurationException($"Could not create '{path}': {ex.Message}", ex);
        }
        finally
        {
            await _fileSystem.DeleteFileIfExistsAsync(staging);
        }
    }

    public async Task CompleteLanguageAsync(DirectoryInfo projectDirectory, IProjectStack stack, LanguageDescriptor language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(language);
        LanguageDescriptor supported = GetDeclaredLanguage(stack, language);
        if (stack.SupportedLanguages.Count < 2)
        {
            throw new ProjectStackConfigurationException($"Stack '{stack.Descriptor.Identifier}' does not require a persisted language.");
        }

        string path = CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory);
        JsonObject? root = ReadRoot(projectDirectory);
        JsonObject? section = root is null ? null : GetStack(root);
        if (section is null
            || !string.Equals(GetString(section, CliConfigurationNames.StackRuntimeKey), stack.Descriptor.Identifier, StringComparison.OrdinalIgnoreCase)
            || GetString(section, CliConfigurationNames.StackLanguageKey) is not null)
        {
            throw new ProjectStackConfigurationException($"Project config '{path}' changed before its language could be completed. Re-run 'func init'.");
        }

        section[CliConfigurationNames.StackRuntimeKey] = stack.Descriptor.Identifier;
        section[CliConfigurationNames.StackLanguageKey] = supported.LanguageIdentifier;
        try
        {
            await _fileSystem.WriteAllTextAsync(path, root!.ToJsonString(_jsonOptions) + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectStackConfigurationException($"Could not update '{path}': {ex.Message}", ex);
        }
    }

    private JsonObject? ReadRoot(DirectoryInfo projectDirectory)
    {
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory);
        try
        {
            using Stream? stream = _fileSystem.OpenReadIfExists(path);
            if (stream is null)
            {
                return null;
            }

            return JsonNode.Parse(stream, _nodeOptions, _documentOptions) as JsonObject
                ?? throw new ProjectStackConfigurationException($"Project config '{path}' must contain a JSON object.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ProjectStackConfigurationException($"Could not read project config '{path}': {ex.Message}", ex);
        }
    }

    private static JsonObject? GetStack(JsonObject root) => root[CliConfigurationNames.StackSectionName] switch
    {
        null => null,
        JsonObject section => section,
        _ => throw new ProjectStackConfigurationException("The 'stack' property in .func/config.json must be an object."),
    };

    private static string? GetString(JsonObject? section, string name)
    {
        if (section is null || !section.TryGetPropertyValue(name, out JsonNode? node))
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue(out string? text) ? text
            : throw new ProjectStackConfigurationException($"The 'stack.{name}' property in .func/config.json must be a string.");
    }

    private static LanguageDescriptor GetDeclaredLanguage(IProjectStack stack, LanguageDescriptor language) =>
        stack.SupportedLanguages.SingleOrDefault(l => l == language) ?? throw new ProjectStackConfigurationException(
            $"Language '{language.LanguageIdentifier}' is not declared by stack '{stack.Descriptor.Identifier}'.");
}
