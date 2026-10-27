// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Captures a stack implementation's immutable capability reports and their project, capture, and evidence identity.
/// A compatibility matcher combines this snapshot with template requirements to evaluate compatibility.
/// </summary>
public sealed class ProjectFacts
{
    public ProjectFacts(ProjectFactsContext context, string evidenceFingerprint, bool isAuthoritative, IEnumerable<ProjectCapability> capabilities)
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
        Array.Sort(captured, static (first, second) =>
        {
            int comparison = StringComparer.Ordinal.Compare(first.Id, second.Id);
            return comparison != 0 ? comparison : first.Support.CompareTo(second.Support);
        });
        Capabilities = Array.AsReadOnly(captured);
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
    /// Gets the stack implementation's SHA-256 fingerprint of all inspected project inputs, including relevant absences.
    /// </summary>
    public string EvidenceFingerprint { get; }

    /// <summary>
    /// Gets whether the stack implementation established this snapshot by inspecting current project inputs rather than inferring support from labels.
    /// A false value prevents the snapshot from establishing either compatibility or incompatibility.
    /// </summary>
    /// <remarks>
    /// This is a stack implementation's assertion, not authentication or independent verification of its input coverage.
    /// </remarks>
    public bool IsAuthoritative { get; }

    public IReadOnlyList<ProjectCapability> Capabilities { get; }

    /// <summary>
    /// Gets a deterministic fingerprint of the complete facts snapshot, including its authority and capture identity.
    /// </summary>
    public string Fingerprint { get; }
}
