// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Reports one Stack's compatibility decision and operation-scoped template bindings.
/// </summary>
public sealed class ProjectTemplateCompatibility : IDisposable
{
    private IDisposable? _evidence;

    /// <summary>
    /// Creates a compatibility result that owns the evidence lease for this operation.
    /// </summary>
    /// <param name="isCompatible">Whether the project supports the selected operation.</param>
    /// <param name="diagnostic">An actionable reason when the project is incompatible.</param>
    /// <param name="bindings">Template binding values evaluated from the project.</param>
    /// <param name="evidence">The pinned source evidence retained until disposal.</param>
    public ProjectTemplateCompatibility(
        bool isCompatible,
        string? diagnostic,
        IReadOnlyDictionary<string, string> bindings,
        IDisposable? evidence)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (!isCompatible)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(evidence);
        }

        IsCompatible = isCompatible;
        Diagnostic = diagnostic;
        Bindings = bindings.ToImmutableDictionary(StringComparer.Ordinal);
        _evidence = evidence;
    }

    /// <summary>
    /// Gets whether the project supports the selected operation.
    /// </summary>
    public bool IsCompatible { get; }

    /// <summary>
    /// Gets an actionable incompatibility diagnostic, or <see langword="null"/> when compatible.
    /// </summary>
    public string? Diagnostic { get; }

    /// <summary>
    /// Gets template binding values evaluated from the project.
    /// </summary>
    public IReadOnlyDictionary<string, string> Bindings { get; }

    /// <summary>
    /// Releases the pinned project evidence.
    /// </summary>
    public void Dispose() => Interlocked.Exchange(ref _evidence, null)?.Dispose();
}
