// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Binds immutable runtime-owned capabilities to the project, capture, and inspected evidence.
/// </summary>
public sealed class ProjectFacts
{
    public ProjectFacts(
        ProjectFactsContext context,
        string evidenceFingerprint,
        bool isAuthoritative,
        IEnumerable<ProjectCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(evidenceFingerprint);
        if (evidenceFingerprint.Length != 64 || !evidenceFingerprint.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Project evidence must have a SHA-256 fingerprint.", nameof(evidenceFingerprint));
        }

        ProjectCapability[] captured = [.. capabilities];
        if (captured.Any(capability => capability is null))
        {
            throw new ArgumentException("Project capabilities must not contain null entries.", nameof(capabilities));
        }

        Context = context;
        EvidenceFingerprint = evidenceFingerprint.ToUpperInvariant();
        IsAuthoritative = isAuthoritative;
        Capabilities = Array.AsReadOnly<ProjectCapability>(
            [.. captured.OrderBy(capability => capability.Id, StringComparer.Ordinal).ThenBy(capability => capability.Support)]);
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Context.ProjectRoot,
            Context.Authority,
            Context.CaptureId,
            EvidenceFingerprint,
            IsAuthoritative,
            Capabilities = Capabilities.Select(capability => new { capability.Id, capability.Support }),
        })));
    }

    public ProjectFactsContext Context { get; }

    /// <summary>
    /// Gets the runtime's SHA-256 fingerprint of all actual-project inputs used to establish the facts, including relevant absences.
    /// </summary>
    public string EvidenceFingerprint { get; }

    public bool IsAuthoritative { get; }

    public IReadOnlyList<ProjectCapability> Capabilities { get; }

    /// <summary>
    /// Gets a deterministic fingerprint of the complete facts snapshot, including its authority and capture identity.
    /// </summary>
    public string Fingerprint { get; }
}
