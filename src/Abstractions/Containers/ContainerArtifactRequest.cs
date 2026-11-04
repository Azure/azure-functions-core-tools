// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Provides a workload with the resolved project and runtime-neutral container generation inputs.
/// </summary>
public sealed record ContainerArtifactRequest
{
    /// <summary>
    /// Parameter name used for the unvalidated runtime version supplied by the user.
    /// </summary>
    public const string RuntimeVersionParameterName = "runtime-version";

    public ContainerArtifactRequest(
        WorkingDirectory projectRoot,
        FunctionsProject project,
        ContainerArtifactCapability capability,
        bool projectInitializationSkipped,
        IReadOnlyDictionary<string, string?> parameters)
    {
        ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
        Project = project ?? throw new ArgumentNullException(nameof(project));
        ArgumentNullException.ThrowIfNull(parameters);

        Parameters = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(parameters, StringComparer.Ordinal));
        Capability = capability;
        ProjectInitializationSkipped = projectInitializationSkipped;
    }

    /// <summary>
    /// Gets the resolved project root.
    /// </summary>
    public WorkingDirectory ProjectRoot { get; }

    /// <summary>
    /// Gets the workload-resolved project.
    /// </summary>
    public FunctionsProject Project { get; }

    /// <summary>
    /// Gets the requested artifact capability.
    /// </summary>
    public ContainerArtifactCapability Capability { get; }

    /// <summary>
    /// Gets whether the command skipped normal project initialization.
    /// </summary>
    public bool ProjectInitializationSkipped { get; }

    /// <summary>
    /// Gets generic user-supplied values keyed by their stable CLI parameter names.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Parameters { get; }
}
