// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Matches the pinned native configuration bytes supported by the controlled .NET workflow adapter.
/// </summary>
internal sealed partial class DotNetTemplateSources(NativeTemplateStackCompatibility? compatibility = null)
{
    private readonly IReadOnlyDictionary<string, string[]> _sources = Load<string[][]>("upstream-configurations.json")
        .ToDictionary(source => source[0], StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, LiteralQualification> _literals = Load<LiteralQualification[]>("binding-literals-qualified.json")
        .ToDictionary(source => source.Identity, StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, LiteralSource> _literalSources = Load<LiteralSource[]>("binding-literals.json")
        .ToDictionary(source => source.Identity, StringComparer.Ordinal);

    public bool Matches(TemplateCatalogEntry entry, string? configurationSha256)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Package?.Identifier.Equals("Azure.Functions.Templates.DotNet", StringComparison.OrdinalIgnoreCase) == true
            && entry.Package.Version is not null && _sources.TryGetValue(entry.Identity, out string[]? source)
            && entry.Type.ToString().Equals(source[1], StringComparison.OrdinalIgnoreCase)
            && entry.Language == source[2] && (Same(configurationSha256, source[3])
                || (_literals.TryGetValue(entry.Identity, out LiteralQualification? literal)
                    && Same(configurationSha256, literal.ConfigurationSha256)));
    }

    /// <summary>
    /// Binds literal rendering to the exact selected source and refuses values unsupported by its installed encoding.
    /// </summary>
    /// <exception cref="InvalidDataException">The source association or binding value cannot be rendered faithfully.</exception>
    public async Task<NativeTemplateSourceSnapshot?> QualifyLiteralsAsync(ResolvedTemplate selected,
        IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_literalSources.TryGetValue(selected.Entry.Identity, out LiteralSource? pin))
        {
            return null;
        }

        if (!Matches(selected.Entry, selected.ConfigurationSha256))
        {
            throw new InvalidDataException("The selected .NET binding configuration is not a qualified native source.");
        }

        string configurationPath = pin.Path[..pin.Path.LastIndexOf('/')] + "/.template.config/template.json";
        if (selected.ConfigurationSourceIdentity?.EndsWith("::/" + configurationPath, StringComparison.Ordinal) != true)
        {
            throw new InvalidDataException("The selected .NET binding configuration is not mounted at its qualified source path.");
        }

        LiteralQualification qualified = _literals[selected.Entry.Identity];
        bool encoded = Same(selected.ConfigurationSha256, qualified.ConfigurationSha256);
        if (pin.Parameters.Any(mapping => parameters.TryGetValue(mapping[0], out string? value)
            && value is not null && !IsWellFormedUnicode(value)))
        {
            throw new InvalidDataException("Binding parameters must contain well-formed Unicode; no files were written.");
        }

        if (!encoded && pin.Parameters.Any(mapping => parameters.TryGetValue(mapping[0], out string? value)
            && value is not null && value.Any(RequiresEncoding)))
        {
            throw UnsupportedLiteral(selected);
        }

        ReadOnlyMemory<byte> configuration = await selected.ReadPackageFileAsync(configurationPath, cancellationToken);
        if (!Same(selected.ConfigurationSha256, Hash(configuration)))
        {
            throw new InvalidDataException("The selected .NET binding configuration does not match its qualified package source path.");
        }

        ReadOnlyMemory<byte> content = await selected.ReadPackageFileAsync(pin.Path, cancellationToken);
        string digest = Hash(content);
        if (!encoded && !qualified.RawSourceCompatible && Same(digest, pin.Sha256))
        {
            throw UnsupportedLiteral(selected);
        }
        bool knownSource = encoded ? qualified.SourceSha256.Any(hash => Same(digest, hash))
            : Same(digest, pin.Sha256) || qualified.LegacySourceSha256.Any(hash => Same(digest, hash));
        if (!knownSource)
        {
            throw new InvalidDataException("The selected .NET binding source changed from the qualified literal authoring contract.");
        }

        TemplateEffectSource source = new(selected.Entry.Identity, pin.Path, digest);
        return new(selected.Entry.Identity, selected.ConfigurationSha256!, [source],
            [new(pin.Path[(pin.Path.LastIndexOf('/') + 1)..], source)]);
    }

    private static bool RequiresEncoding(char value) => value is '\\' or '"' or '\u0085' or '\u2028' or '\u2029'
        || char.IsControl(value);

    private InvalidDataException UnsupportedLiteral(ResolvedTemplate selected)
        => new($"The selected {selected.Entry.Package!.Identifier} {selected.Entry.Package.Version} item cannot preserve this binding value in source code. "
            + (compatibility?.UpdateHint("dotnet") ?? "Identify the installed workload that owns these selected templates and update that owner explicitly.")
            + " No files were written.");

    private static bool IsWellFormedUnicode(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
            {
                continue;
            }
            if (!char.IsHighSurrogate(value[index]) || index + 1 == value.Length || !char.IsLowSurrogate(value[++index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string Hash(ReadOnlyMemory<byte> content) => Convert.ToHexString(SHA256.HashData(content.Span));

    private static T Load<T>(string name)
    {
        using Stream stream = typeof(DotNetTemplateSources).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Templates.DotNet." + name)
            ?? throw new InvalidOperationException("The qualified .NET template source inventory is missing.");
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The qualified .NET template source inventory is empty.");
    }

    private sealed record LiteralSource(string Identity, string Path, string Sha256, string[][] Parameters);

    private sealed record LiteralQualification(string Identity, string ConfigurationSha256, string[] SourceSha256,
        string[] LegacySourceSha256, bool RawSourceCompatible = true);
}
