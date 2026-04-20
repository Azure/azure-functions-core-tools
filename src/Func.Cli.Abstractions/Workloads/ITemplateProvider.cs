// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Provides function templates for 'func new'. Workloads implement this
/// to contribute language-specific templates (e.g., HttpTrigger for Python).
/// </summary>
public interface ITemplateProvider
{
    /// <summary>
    /// The worker runtime this provider supports (e.g., "dotnet", "python", "node").
    /// </summary>
    public string WorkerRuntime { get; }

    /// <summary>
    /// Lists all available templates from this provider.
    /// </summary>
    public Task<IReadOnlyList<FunctionTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Scaffolds a function from the specified template into the target directory.
    /// </summary>
    public Task ScaffoldAsync(FunctionScaffoldContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context passed to <see cref="ITemplateProvider.ScaffoldAsync"/>.
/// </summary>
/// <param name="TemplateName">The selected template name (e.g., "HttpTrigger").</param>
/// <param name="FunctionName">The name for the new function.</param>
/// <param name="OutputPath">The directory to scaffold into.</param>
/// <param name="Language">Optional language (e.g., "C#", "F#", "JavaScript").</param>
/// <param name="AuthLevel">Optional authorization level for HTTP triggers.</param>
/// <param name="Force">Whether to overwrite existing files.</param>
public record FunctionScaffoldContext(
    string TemplateName,
    string FunctionName,
    string OutputPath,
    string? Language = null,
    string? AuthLevel = null,
    bool Force = false);

/// <summary>
/// Describes a function template available for 'func new'.
/// </summary>
/// <param name="Name">Template name (e.g., "HttpTrigger").</param>
/// <param name="Description">Brief description of the template.</param>
/// <param name="WorkerRuntime">The runtime this template targets.</param>
/// <param name="Language">Optional language variant (e.g., "C#", "JavaScript").</param>
public record FunctionTemplate(
    string Name,
    string Description,
    string WorkerRuntime,
    string? Language = null);
