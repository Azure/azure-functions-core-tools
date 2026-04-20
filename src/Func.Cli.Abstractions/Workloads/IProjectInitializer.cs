// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Handles project initialization for a specific worker runtime.
/// Workloads implement this to provide 'func init' support for their language stack.
/// </summary>
public interface IProjectInitializer
{
    /// <summary>
    /// The worker runtime this initializer supports (e.g., "dotnet", "python", "node").
    /// </summary>
    public string WorkerRuntime { get; }

    /// <summary>
    /// Returns the supported languages/variants for this runtime
    /// (e.g., "C#", "F#" for dotnet; "JavaScript", "TypeScript" for node).
    /// </summary>
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Returns additional CLI options this workload contributes to 'func init'.
    /// These are added to the init command at startup so they appear in help
    /// and can be parsed. For example, dotnet might add --target-framework,
    /// while python might add --model.
    /// </summary>
    public IReadOnlyList<Option> GetInitOptions();

    /// <summary>
    /// Returns true if this initializer can handle the given worker runtime.
    /// </summary>
    public bool CanHandle(string workerRuntime);

    /// <summary>
    /// Initializes a new Functions project in the specified directory.
    /// </summary>
    /// <param name="context">Universal init context (path, runtime, name, force).</param>
    /// <param name="parseResult">The full parse result, so the initializer can read its own contributed options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(ProjectInitContext context, ParseResult parseResult, CancellationToken cancellationToken = default);
}

/// <summary>
/// Universal context passed to <see cref="IProjectInitializer.InitializeAsync"/>.
/// Contains only the options common to all runtimes. Workload-specific options
/// are read directly from the ParseResult by each initializer.
/// </summary>
/// <param name="ProjectPath">The directory to initialize the project in.</param>
/// <param name="WorkerRuntime">The selected worker runtime.</param>
/// <param name="Language">The selected language (optional, may be prompted interactively).</param>
/// <param name="ProjectName">The project name (optional).</param>
/// <param name="Force">Whether to overwrite existing files.</param>
public record ProjectInitContext(
    string ProjectPath,
    string WorkerRuntime,
    string? Language = null,
    string? ProjectName = null,
    bool Force = false);
