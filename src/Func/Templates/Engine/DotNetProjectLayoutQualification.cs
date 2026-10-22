// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Pins an explicitly selected .NET project configuration for the bounded, internal layout adapter.
/// </summary>
internal sealed record DotNetProjectLayoutQualification
{
    public DotNetProjectLayoutQualification(string templateIdentity, string configurationFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFingerprint);
        if (configurationFingerprint.Length != 64 || !configurationFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Project layout qualification requires an exact configuration SHA-256.", nameof(configurationFingerprint));
        }

        TemplateIdentity = templateIdentity;
        ConfigurationFingerprint = configurationFingerprint;
    }

    public string TemplateIdentity { get; }

    public string ConfigurationFingerprint { get; }
}
