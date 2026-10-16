// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Freezes selected package sources and the native file sources authorized by an owned workflow.
/// </summary>
internal sealed class NativeTemplateSourceSnapshot
{
    public NativeTemplateSourceSnapshot(string templateIdentity, string configurationSha256,
        IEnumerable<TemplateEffectSource> sources, IEnumerable<NativeTemplateOutputSource> outputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSha256);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(outputs);
        TemplateEffectSource[] pins = [.. sources];
        NativeTemplateOutputSource[] files = [.. outputs];
        if (!Hash(configurationSha256) || pins.Length == 0 || files.Length == 0
            || pins.Any(pin => pin is null || pin.TemplateIdentity != templateIdentity || !Relative(pin.Identity) || !Hash(pin.Sha256))
            || pins.Select(pin => pin.Identity).Distinct(StringComparer.Ordinal).Count() != pins.Length
            || files.Any(file => file is null || file.SourceRelativePath is null
                || !Relative(Normalize(file.SourceRelativePath)) || !pins.Contains(file.Source))
            || files.Select(file => Normalize(file.SourceRelativePath)).Distinct(StringComparer.Ordinal).Count() != files.Length)
        {
            throw new ArgumentException("Source qualification requires unique selected-package SHA-256 pins and exact regular-file sources.");
        }

        TemplateIdentity = templateIdentity;
        ConfigurationSha256 = configurationSha256;
        Sources = Array.AsReadOnly(pins);
        Outputs = Array.AsReadOnly(files);
    }

    public string TemplateIdentity { get; }

    public string ConfigurationSha256 { get; }

    public IReadOnlyList<TemplateEffectSource> Sources { get; }

    public IReadOnlyList<NativeTemplateOutputSource> Outputs { get; }

    internal static string Normalize(string path) => path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;

    private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool Relative(string value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value)
        && !value.Contains('\\') && value.Split('/').All(part => part is not ("" or "." or "..") && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
}

internal sealed record NativeTemplateOutputSource(string SourceRelativePath, TemplateEffectSource Source);
