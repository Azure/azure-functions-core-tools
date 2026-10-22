// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Native;

/// <summary>
/// Provides immutable facts and source access for the host's already selected native template.
/// </summary>
/// <remarks>
/// The host supplies this view. Policies must not select another installed package or retain invocation state on shared services.
/// </remarks>
public abstract class NativeTemplateSource
{
    public abstract string Identity { get; }

    public abstract string DisplayName { get; }

    public abstract bool IsProject { get; }

    public abstract string? Language { get; }

    public abstract IReadOnlyList<string> ShortNames { get; }

    public abstract bool IsHidden { get; }

    public abstract string? InternalRole { get; }

    public abstract string? DefaultName { get; }

    public abstract string PackageId { get; }

    public abstract string PackageVersion { get; }

    public abstract string ConfigurationSha256 { get; }

    public abstract IReadOnlyList<TemplateUserPrompt> CanonicalParameters { get; }

    /// <summary>
    /// Reads an exact package-relative path from the same captured selected source.
    /// </summary>
    /// <exception cref="ArgumentException">The path is not an exact package-relative path.</exception>
    /// <exception cref="IOException">The captured source cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The captured source cannot be accessed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public abstract Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Revalidates the captured selected source, configuration and workload ownership, throwing when they have changed.
    /// </summary>
    /// <exception cref="InvalidDataException">The captured source or admitted ownership changed.</exception>
    /// <exception cref="IOException">The captured source or ownership cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The captured source or ownership cannot be accessed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public abstract Task RevalidateAsync(CancellationToken cancellationToken);
}
