// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Narrows immutable neutral descriptors without native registration, compatibility admission, or execution.
/// </summary>
internal sealed class TemplateSelector(TemplateArgumentParser argumentParser)
{
    private readonly TemplateArgumentParser _argumentParser = argumentParser ?? throw new ArgumentNullException(nameof(argumentParser));

    /// <summary>
    /// Matches identity before aliases, enforces type and group boundaries, then applies the requested variant filters.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">Descriptor identities are duplicated or fallback aliases conflict.</exception>
    public TemplateSelectionResult Select(ImmutableArray<TemplateDescriptor> descriptors, TemplateSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference, nameof(request));
        if (!Enum.IsDefined(request.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A recognized template type is required.");
        }

        if (request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
        {
            throw new ArgumentException("A supplied language must be nonblank.", nameof(request));
        }

        TemplateArgumentParser.ValidateInputs(request.Tokens, request.ReservedAliases);
        if (descriptors.IsDefault || descriptors.Any(descriptor => descriptor is null))
        {
            throw new ArgumentException("Descriptors must be initialized and cannot contain null.", nameof(descriptors));
        }

        ImmutableArray<TemplateDescriptor> ordered = [.. descriptors.OrderBy(descriptor => descriptor.Metadata.Identity, StringComparer.Ordinal)];
        string[] duplicates = [.. ordered.GroupBy(descriptor => descriptor.Metadata.Identity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key)];
        if (duplicates.Length > 0)
        {
            throw new InvalidTemplateMetadataException($"The catalog repeats template identities: {string.Join(", ", duplicates)}. Correct the package metadata.");
        }

        ImmutableArray<TemplateDescriptor> matches = [.. ordered.Where(
            descriptor => descriptor.Metadata.Identity.Equals(request.Reference, StringComparison.Ordinal))];
        if (matches.IsEmpty)
        {
            matches = [.. ordered.Where(descriptor => descriptor.Metadata.ShortNames.Contains(request.Reference, StringComparer.OrdinalIgnoreCase))];
        }

        if (matches.IsEmpty)
        {
            return new(TemplateSelectionStatus.NotFound, [], [], [$"Template '{request.Reference}' was not found. Supply an exact identity or short name."]);
        }

        ImmutableArray<TemplateDescriptor> candidates = [.. matches.Where(descriptor => descriptor.Metadata.Type == request.Type)];
        if (candidates.IsEmpty)
        {
            return new(TemplateSelectionStatus.WrongType, matches, [], [$"Template '{request.Reference}' does not identify a '{request.Type}' template."]);
        }

        int groupCount = candidates.Select(descriptor => (
            HasGroup: descriptor.Metadata.GroupIdentity is not null,
            Identity: descriptor.Metadata.GroupIdentity ?? descriptor.Metadata.Identity)).Distinct().Count();
        if (groupCount > 1)
        {
            return new(TemplateSelectionStatus.AmbiguousGroup, candidates, [],
                [$"Short name '{request.Reference}' identifies multiple template groups. Supply an exact template identity."]);
        }

        if (request.Language is { } language)
        {
            candidates = [.. candidates.Where(descriptor => string.Equals(descriptor.Metadata.Language, language, StringComparison.OrdinalIgnoreCase))];
            if (candidates.IsEmpty)
            {
                return new(TemplateSelectionStatus.UnsatisfiedLanguage, [], [],
                    [$"Template '{request.Reference}' has no variant for language '{language}'."]);
            }
        }

        ImmutableArray<TemplateArgumentParseResult> parsed = [.. candidates.Select(
            candidate => _argumentParser.Parse(candidate, request.Tokens, request.ReservedAliases))];
        candidates = [.. parsed.Where(result => result.IsArgumentCompatible).Select(result => result.Template)];
        if (candidates.IsEmpty)
        {
            return new(TemplateSelectionStatus.InvalidArguments, [], parsed,
                [.. parsed.SelectMany(result => result.Diagnostics.Select(diagnostic => $"Template '{result.Template.Metadata.Identity}': {diagnostic}"))]);
        }

        if (request.ApplyPrecedence)
        {
            int precedence = candidates.Max(candidate => candidate.Metadata.Precedence);
            candidates = [.. candidates.Where(candidate => candidate.Metadata.Precedence == precedence)];
        }

        return candidates.Length == 1
            ? new(TemplateSelectionStatus.Selected, candidates, parsed, [])
            : new(TemplateSelectionStatus.Ambiguous, candidates, parsed,
                [$"Template '{request.Reference}' has multiple matching variants. Supply an exact template identity."]);
    }
}
