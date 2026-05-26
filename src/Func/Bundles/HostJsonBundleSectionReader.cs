// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Filesystem-backed host.json extension bundle reader.
/// </summary>
internal sealed class HostJsonBundleSectionReader : IHostJsonBundleSectionReader
{
    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<HostJsonBundleSection?> ReadAsync(DirectoryInfo projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        string hostJsonPath = Path.Combine(projectDirectory.FullName, "host.json");
        if (!File.Exists(hostJsonPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                hostJsonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, _jsonOptions, cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExtensionBundleConfigurationException(
                    $"host.json root must be a JSON object: '{hostJsonPath}'. Fix host.json and run 'func start' again.");
            }

            if (!document.RootElement.TryGetProperty("extensionBundle", out JsonElement bundle))
            {
                return null;
            }

            if (bundle.ValueKind != JsonValueKind.Object)
            {
                throw CreateInvalidBundleSection(hostJsonPath);
            }

            string bundleId = ReadRequiredString(bundle, "id", hostJsonPath);
            string bundleVersion = ReadRequiredString(bundle, "version", hostJsonPath);
            return new HostJsonBundleSection(bundleId, bundleVersion);
        }
        catch (JsonException ex)
        {
            throw new ExtensionBundleConfigurationException(
                $"host.json is not valid JSON: '{hostJsonPath}'. Fix host.json and run 'func start' again.",
                ex);
        }
        catch (IOException ex)
        {
            throw new ExtensionBundleConfigurationException(
                $"Could not read host.json: '{hostJsonPath}'. Check file permissions and run 'func start' again.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ExtensionBundleConfigurationException(
                $"Could not read host.json: '{hostJsonPath}'. Check file permissions and run 'func start' again.",
                ex);
        }
    }

    private static string ReadRequiredString(JsonElement bundle, string propertyName, string hostJsonPath)
    {
        if (!bundle.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw CreateInvalidBundleSection(hostJsonPath);
        }

        string? value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw CreateInvalidBundleSection(hostJsonPath);
        }

        return value;
    }

    private static ExtensionBundleConfigurationException CreateInvalidBundleSection(string hostJsonPath)
        => new(
            $"host.json extensionBundle must include non-empty string 'id' and 'version' values: '{hostJsonPath}'. "
            + "Fix host.json and run 'func start' again.");
}
