// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

internal sealed partial class DotNetTemplateSources
{
    private readonly JsonElement[] _auxiliarySources = LoadAuxiliarySources();

    internal async Task<NativeTemplateSourceSnapshot?> QualifyAuxiliaryAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        JsonElement[] candidates = [.. _auxiliarySources.Where(pin => pin.GetProperty("identity").GetString() == selected.Entry.Identity)];
        if (candidates.Length == 0 || selected.Entry.Language != "C#" || selected.Entry.Type != TemplateType.Item)
        {
            return null;
        }
        if (candidates.Length != 1 || !Matches(selected.Entry, selected.ConfigurationSha256))
        {
            throw new InvalidDataException("The selected auxiliary item has no unique qualified .NET configuration.");
        }
        JsonElement pin = candidates[0];
        string path = pin.GetProperty("path").GetString()!;
        string configurationPath = path[..path.LastIndexOf('/')] + "/.template.config/template.json";
        if (selected.ConfigurationSourceIdentity?.EndsWith("::/" + configurationPath, StringComparison.Ordinal) != true)
        {
            throw new InvalidDataException("The selected .NET auxiliary configuration is not mounted at its qualified source path.");
        }
        ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(path, cancellationToken);
        string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
        if ((Same(hash, pin.GetProperty("sha256").GetString())
            && Same(selected.ConfigurationSha256, pin.GetProperty("configurationSha256").GetString()))
            || (pin.TryGetProperty("compatibleSources", out JsonElement compatible)
                && compatible.EnumerateArray().Any(source => Same(hash, source.GetProperty("sha256").GetString())
                    && Same(selected.ConfigurationSha256, source.GetProperty("configurationSha256").GetString()))))
        {
            // Compatible older packages retain their authored helper names and limitations.
            return null;
        }
        if (!pin.GetProperty("authoredSources").EnumerateArray().Any(source =>
            Same(hash, source.GetProperty("sha256").GetString())
            && Same(selected.ConfigurationSha256, source.GetProperty("configurationSha256").GetString())))
        {
            throw new InvalidDataException("The selected .NET auxiliary source changed or is unsupported. Reinstall its qualified workload package.");
        }
        TemplateEffectSource sourcePin = new(selected.Entry.Identity, path, hash);
        return new(selected.Entry.Identity, selected.ConfigurationSha256!, [sourcePin], [new(Path.GetFileName(path), sourcePin)]);
    }

    internal static NativeTemplateSourceSnapshot? CombineAuxiliarySources(
        NativeTemplateSourceSnapshot? existing, NativeTemplateSourceSnapshot? auxiliary)
    {
        if (existing is null || auxiliary is null)
        {
            return existing ?? auxiliary;
        }
        if (existing.TemplateIdentity != auxiliary.TemplateIdentity || !Same(existing.ConfigurationSha256, auxiliary.ConfigurationSha256)
            || !existing.Outputs.Select(output => output.SourceRelativePath).Order(StringComparer.Ordinal)
                .SequenceEqual(auxiliary.Outputs.Select(output => output.SourceRelativePath).Order(StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The selected .NET literal and auxiliary source qualifications disagree.");
        }
        List<TemplateEffectSource> sources = [.. existing.Sources];
        foreach (TemplateEffectSource source in auxiliary.Sources)
        {
            TemplateEffectSource? previous = sources.SingleOrDefault(pin => pin.Identity == source.Identity);
            if (previous is not null && !Same(previous.Sha256, source.Sha256))
            {
                throw new InvalidDataException("The selected .NET literal and auxiliary source hashes disagree.");
            }
            if (previous is null)
            {
                sources.Add(source);
            }
        }
        return new(existing.TemplateIdentity, existing.ConfigurationSha256, sources, existing.Outputs);
    }

    private static JsonElement[] LoadAuxiliarySources()
    {
        using Stream stream = typeof(DotNetTemplateSources).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Templates.DotNet.auxiliary-identities.json")
            ?? throw new InvalidOperationException("The qualified .NET auxiliary source inventory is missing.");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.EnumerateArray().Select(pin => pin.Clone())];
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
