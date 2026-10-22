// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Pins a source-declared project anchor before catalog eligibility and actual native capture.
/// </summary>
internal sealed record NativeProjectLayoutQualification
{
    public NativeProjectLayoutQualification(string templateIdentity, string configurationFingerprint,
        Native.NativeTemplateProjectAnchor anchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFingerprint);
        ArgumentNullException.ThrowIfNull(anchor);
        if (configurationFingerprint.Length != 64 || !configurationFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Project layout qualification requires an exact configuration SHA-256.", nameof(configurationFingerprint));
        }

        TemplateIdentity = templateIdentity;
        ConfigurationFingerprint = configurationFingerprint;
        Anchor = new(anchor.PrimaryOutputIndex, anchor.Stack, anchor.Language);
    }

    public string TemplateIdentity { get; }

    public string ConfigurationFingerprint { get; }

    public Native.NativeTemplateProjectAnchor Anchor { get; }

    internal NativeTemplatePolicyCandidate? Policy { get; init; }
}
