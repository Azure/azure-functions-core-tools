// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Filesystem-backed cache for the quickstart manifest.
/// </summary>
internal sealed class ManifestCache(IOptions<QuickstartManifestOptions> options) : IManifestCache
{
    private const string ManifestFileName = "manifest.json";
    private const string MetaFileName = "manifest-meta.json";

    private readonly QuickstartManifestOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    private string ManifestFilePath => Path.Combine(_options.CacheDirectory, ManifestFileName);

    private string MetaFilePath => Path.Combine(_options.CacheDirectory, MetaFileName);

    /// <inheritdoc/>
    public void EnsureDirectory() => Directory.CreateDirectory(_options.CacheDirectory);

    /// <inheritdoc/>
    public string? TryReadManifest()
    {
        try
        {
            return File.Exists(ManifestFilePath) ? File.ReadAllText(ManifestFilePath) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void WriteManifest(string json) => AtomicWrite(ManifestFilePath, json);

    /// <inheritdoc/>
    public ManifestCacheMeta? TryReadMeta()
    {
        try
        {
            if (!File.Exists(MetaFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(MetaFilePath);
            return JsonSerializer.Deserialize(json, QuickstartJsonContext.Default.ManifestCacheMeta);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void WriteMeta(ManifestCacheMeta meta)
    {
        string json = JsonSerializer.Serialize(meta, QuickstartJsonContext.Default.ManifestCacheMeta);
        AtomicWrite(MetaFilePath, json);
    }

    /// <inheritdoc/>
    public bool ManifestExists() => File.Exists(ManifestFilePath);

    private static void AtomicWrite(string destinationPath, string content)
    {
        string tempPath = $"{destinationPath}.{Path.GetRandomFileName()}.tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup of the unique temp file before rethrowing the original failure.
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Ignore: the temp file is uniquely named so a leftover is harmless, and we
                // must not mask the original write/move exception being rethrown below.
            }

            throw;
        }
    }
}
