// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Microsoft.TemplateEngine.Abstractions;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Preflights downloaded package metadata and verifies identities after managed discovery refresh.
/// </summary>
internal sealed class TemplatePackageValidator
{
    public IReadOnlyList<string> Validate(Stream archive, string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);
        try
        {
            return ValidateCore(archive, packageId, version, cancellationToken);
        }

        catch (JsonException ex)
        {
            throw new TemplatePackageException("The archive contains invalid template or func.host.json metadata.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new TemplatePackageException("The downloaded file is not a valid NuGet archive.", ex);
        }
        catch (PackagingException ex)
        {
            throw new TemplatePackageException("The archive contains invalid NuGet package metadata.", ex);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new TemplatePackageException("The archive contains invalid NuGet package XML.", ex);
        }
    }

    public void VerifyInstalled(IEnumerable<ITemplateInfo> templates, IReadOnlyList<string> identities, IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(settings);
        ITemplateInfo[] installed = [.. templates];
        string[] discovered = [.. installed.Select(t => t.Identity)];
        if (discovered.Length != identities.Count || identities.Except(discovered, StringComparer.Ordinal).Any())
        {
            throw new TemplatePackageException("The installed template cache did not expose the validated package.");
        }

        foreach (ITemplateInfo template in installed)
        {
            _ = FuncTemplateInfo.Create(template, settings);
        }
    }

    public void VerifyUninstalled(IEnumerable<ITemplateInfo> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        if (templates.Any())
        {
            throw new TemplatePackageException("The template cache still contains the uninstalled package.");
        }
    }

    private IReadOnlyList<string> ValidateCore(Stream archive, string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        using PackageArchiveReader reader = new(archive, leaveStreamOpen: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(reader.NuspecReader.GetId(), packageId, StringComparison.OrdinalIgnoreCase)
            || reader.NuspecReader.GetVersion() != version)
        {
            throw new TemplatePackageException("The downloaded archive does not match the requested package ID and version.");
        }

        IReadOnlyList<PackageType> types = reader.NuspecReader.GetPackageTypes();
        if (!types.Any(t => string.Equals(t.Name, TemplateCatalog.PackageType, StringComparison.OrdinalIgnoreCase))
            || types.Any(t => string.Equals(t.Name, WorkloadPackageTypes.Workload, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Name, WorkloadPackageTypes.RuntimeIdentifierPackage, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TemplatePackageException("The archive must declare FuncTemplate and must not also declare a workload package type.");
        }

        List<string> identities = [];
        foreach (string path in reader.GetFiles().Where(p => p.EndsWith(".template.config/template.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream content = reader.GetStream(path);
            using var declaration = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            JsonElement root = declaration.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("identity", out JsonElement identity) || identity.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(identity.GetString()))
            {
                continue;
            }

            identities.Add(identity.GetString()!);
        }

        foreach (string path in reader.GetFiles().Where(p => p.EndsWith(".template.config/func.host.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream content = reader.GetStream(path);
            using StreamReader text = new(content);
            _ = FuncTemplateHostData.Parse(text.ReadToEnd());
        }

        if (identities.Count == 0)
        {
            throw new TemplatePackageException("The archive must contain at least one template identity.");
        }

        return [.. identities.Distinct(StringComparer.Ordinal)];
    }
}
