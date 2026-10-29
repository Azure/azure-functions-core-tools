// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Retains one candidate binding per descriptor until the owning operation releases the native session.
/// </summary>
internal abstract class PackageTemplateSession : IAsyncDisposable
{
    protected PackageTemplateSession(IEnumerable<PackageTemplateCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = [.. candidates];
        HashSet<TemplateDescriptor> descriptors = new(ReferenceEqualityComparer.Instance);
        foreach (PackageTemplateCandidate candidate in Candidates)
        {
            if (candidate is null || !descriptors.Add(candidate.Descriptor))
            {
                throw new InvalidTemplateMetadataException("Each package candidate must have its own non-null descriptor binding.");
            }
        }
    }

    public ImmutableArray<PackageTemplateCandidate> Candidates { get; }

    public abstract ValueTask DisposeAsync();
}
