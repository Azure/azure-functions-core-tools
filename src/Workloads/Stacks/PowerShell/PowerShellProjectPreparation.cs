// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Azure.Functions.Cli.Projects;

#if FUNC_POWERSHELL_TEMPLATE_HELPERS
namespace Azure.Functions.Cli.Templates.PowerShell.Shared;
#else
namespace Azure.Functions.Cli.Workloads.PowerShell;
#endif

internal sealed record PowerShellManagedDependencyContent(
    string? Content, string? SourceIdentity, string? SourceSha256, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Resolves owned managed-dependency content without writing files or invoking the project initializer.
/// </summary>
internal class PowerShellProjectPreparation
{
    internal const string RequirementsSourceIdentity = "Azure.Functions.Cli.Workloads.PowerShell.Templates.requirements.psd1";
    private const string GalleryUri = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Az'";

    public async Task<PowerShellManagedDependencyContent> PrepareManagedDependenciesAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled)
        {
            return new(null, null, null, []);
        }

        string source = ReadRequirementsTemplate();
        string? major = await GetLatestAzModuleMajorVersionAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string content = major is null ? source : source.Replace("MAJOR_VERSION", major, StringComparison.Ordinal);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return new(content, RequirementsSourceIdentity, hash, major is null
            ? ["Could not resolve the latest stable Az module major version. The requirements.psd1 Az entry remains commented with MAJOR_VERSION; set a supported version before enabling it."]
            : []);
    }

    public virtual string ReadRequirementsTemplate()
        => ProjectFiles.ReadTemplate(typeof(PowerShellProjectPreparation).Assembly, "requirements.psd1");

    public virtual async Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "AzureFunctionsCli");
            client.Timeout = TimeSpan.FromSeconds(10);
            using HttpResponseMessage response = await client.GetAsync(new Uri(GalleryUri), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            XmlDocument doc = new();
            using (var reader = XmlReader.Create(stream))
            {
                doc.Load(reader);
            }

            XmlNamespaceManager nsmgr = new(doc.NameTable);
            nsmgr.AddNamespace("ps", "http://www.w3.org/2005/Atom");
            nsmgr.AddNamespace("d", "http://schemas.microsoft.com/ado/2007/08/dataservices");
            nsmgr.AddNamespace("m", "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");
            XmlNodeList? versions = doc.DocumentElement?.SelectNodes("//m:properties[d:IsPrerelease = \"false\"]/d:Version", nsmgr);
            var latestVersion = new Version("0.0");
            if (versions is { Count: > 0 })
            {
                foreach (XmlNode version in versions)
                {
                    if (Version.TryParse(version.FirstChild?.Value, out Version? currentVersion) && currentVersion > latestVersion)
                    {
                        latestVersion = currentVersion;
                    }
                }
            }

            string major = latestVersion.Major.ToString();
            return major == "0" ? null : major;
        }
        catch (Exception)
        {
            // Preserve the legacy optional lookup fallback; the prepared result exposes it and rechecks cancellation.
            return null;
        }
    }
}
