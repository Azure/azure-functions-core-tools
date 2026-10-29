// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Binds a validated descriptor and native eligibility to the exact retained candidate that renders it.
/// </summary>
internal abstract class PackageTemplateCandidate
{
    protected PackageTemplateCandidate(
        TemplateDescriptor descriptor, FuncTemplateConstraintOutcome eligibility, IEnumerable<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!Enum.IsDefined(eligibility))
        {
            throw new ArgumentOutOfRangeException(nameof(eligibility));
        }

        Descriptor = descriptor;
        Eligibility = eligibility;
        Diagnostics = [.. diagnostics ?? []];
        if (Diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Native diagnostics cannot contain null entries.", nameof(diagnostics));
        }
    }

    public TemplateDescriptor Descriptor { get; }

    public FuncTemplateConstraintOutcome Eligibility { get; }

    public ImmutableArray<string> Diagnostics { get; }

    public Task<PackageTemplateCapture> RenderAsync(
        TemplateArgumentParseResult arguments, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(arguments.Template, Descriptor) || !arguments.IsComplete)
        {
            throw new ArgumentException("Rendering requires complete arguments parsed for this exact candidate.", nameof(arguments));
        }

        EnsureEligible();
        return RenderCoreAsync(arguments, name, cancellationToken);
    }

    internal void EnsureEligible()
    {
        if (Descriptor.Metadata is { Type: TemplateType.Item, DeclaredInternalRole: not null })
        {
            throw new TemplatePreparationException(
                TemplatePreparationFailure.InternalTemplate,
                $"Template '{Descriptor.Metadata.Identity}' declares an internal role and cannot be invoked as an ordinary function template.");
        }

        if (Eligibility != FuncTemplateConstraintOutcome.Eligible)
        {
            throw new TemplatePreparationException(
                TemplatePreparationFailure.NativeIneligible,
                $"Template '{Descriptor.Metadata.Identity}' is {Eligibility} in the native context.",
                diagnostics: Diagnostics);
        }
    }

    /// <summary>
    /// Renders once in private state and captures actual bytes and verified configuration intent, never writing the target.
    /// </summary>
    protected abstract Task<PackageTemplateCapture> RenderCoreAsync(
        TemplateArgumentParseResult arguments, string? name, CancellationToken cancellationToken);
}

/// <summary>
/// Snapshots actual rendered output and any already serialized CLI configuration before publication.
/// </summary>
internal sealed class PackageTemplateCapture(IEnumerable<PreparedOutputEntry> entries, PreparedConfiguration? configuration = null)
{
    public ImmutableArray<PreparedOutputEntry> Entries { get; } = [.. entries ?? throw new ArgumentNullException(nameof(entries))];

    public PreparedConfiguration? Configuration { get; } = configuration;
}
