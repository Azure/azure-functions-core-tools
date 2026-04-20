// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Handles packaging a Functions project for deployment.
/// Workloads implement this to provide 'func pack' support for their language stack.
/// </summary>
public interface IPackProvider
{
    /// <summary>
    /// The worker runtime this provider supports (e.g., "dotnet", "python", "node").
    /// </summary>
    public string WorkerRuntime { get; }

    /// <summary>
    /// Returns additional CLI options this workload contributes to 'func pack'.
    /// These are added to the pack command at startup so they appear in help
    /// and can be parsed.
    /// </summary>
    public IReadOnlyList<Option> GetPackOptions() => [];

    /// <summary>
    /// Validates the project structure before packaging.
    /// Throws <see cref="GracefulException"/> if validation fails.
    /// </summary>
    public Task ValidateAsync(PackContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Prepares the project for packaging (e.g., runs dotnet publish or npm build).
    /// Returns the root directory to zip. If <see cref="PackContext.NoBuild"/> is true,
    /// should skip the build step and return the appropriate directory to package.
    /// </summary>
    public Task<string> PrepareAsync(PackContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optional cleanup after packaging (e.g., removing temporary build output).
    /// </summary>
    public Task CleanupAsync(PackContext context, string packingRoot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Context passed to <see cref="IPackProvider"/>.
/// </summary>
/// <param name="ProjectPath">The root directory of the Functions project.</param>
/// <param name="OutputPath">The output path for the zip file. Null for default.</param>
/// <param name="NoBuild">Whether to skip the build step.</param>
/// <param name="AdditionalArgs">Any additional arguments passed by the user.</param>
public record PackContext(
    string ProjectPath,
    string? OutputPath = null,
    bool NoBuild = false,
    IReadOnlyList<string>? AdditionalArgs = null);
