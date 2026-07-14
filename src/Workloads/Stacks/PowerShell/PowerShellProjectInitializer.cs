// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json.Nodes;

using System.Xml;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.PowerShell;

/// <summary>
/// Scaffolds a PowerShell Functions project (<c>host.json</c>, <c>local.settings.json</c>,
/// <c>profile.ps1</c>, <c>.gitignore</c>) and optionally enables managed dependencies
/// with a <c>requirements.psd1</c>.
/// </summary>
internal sealed class PowerShellProjectInitializer : IProjectInitializer
{
    private const string AzModuleName = "Az";
    private const string PowerShellGalleryFindPackagesByIdUri = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id=";
    private const int GalleryTimeoutSeconds = 10;
    private const string MajorVersionPlaceholder = "MAJOR_VERSION";
    private const string RuntimeVersionPlaceholder = "{RuntimeVersion}";
    private const string DefaultRuntimeVersion = "7.4";

    private static readonly string[] _supportedRuntimeVersions = ["7.4", "7.6"];

    private static readonly Assembly _assembly = typeof(PowerShellProjectInitializer).Assembly;

    // Internal seam so tests can stub the Az module version lookup.
    internal Func<CancellationToken, Task<string?>> GetLatestAzModuleMajorVersion { get; set; } = DefaultGetLatestAzModuleMajorVersion;

    public string Stack => "powershell";

    public string DisplayName => "PowerShell";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
        new Dictionary<string, IReadOnlyList<string>>()
        {
            { "PowerShell", ["pwsh", "ps"] }
        };

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public Option<bool> ManagedDependenciesOption { get; private set; } = default!;

    public Option<string> RuntimeVersionOption { get; private set; } = default!;

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        ManagedDependenciesOption = registry.GetOrAdd(new Option<bool>("--managed-dependencies")
        {
            Description = "Enable managed dependencies for the PowerShell function app.",
            DefaultValueFactory = _ => false,
        });

        RuntimeVersionOption = registry.GetOrAdd(new Option<string>("--runtime-version")
        {
            Description = $"PowerShell runtime version. Supported: {string.Join(", ", _supportedRuntimeVersions)}.",
            DefaultValueFactory = _ => DefaultRuntimeVersion,
        });

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        return [ManagedDependenciesOption, RuntimeVersionOption, NoBundleOption, BundlesChannelOption];
    }

    public async Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();

        string root = context.WorkingDirectory.Info.FullName;
        bool force = context.Force;
        bool managedDependencies = parseResult.GetValue(ManagedDependenciesOption);
        string runtimeVersion = parseResult.GetValue(RuntimeVersionOption) ?? DefaultRuntimeVersion;
        bool noBundle = parseResult.GetValue(NoBundleOption);
        BundleChannel channel = parseResult.GetValue(BundlesChannelOption);

        if (!_supportedRuntimeVersions.Contains(runtimeVersion))
        {
            throw new ArgumentException(
                $"PowerShell runtime version '{runtimeVersion}' is not supported. Supported versions: {string.Join(", ", _supportedRuntimeVersions)}.",
                nameof(runtimeVersion));
        }

        // host.json
        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "host.json"),
            ProjectFiles.MinimalHostJson,
            force);

        if (!noBundle)
        {
            ProjectFiles.MergeHostJson(
                Path.Combine(root, "host.json"),
                host => EnsureExtensionBundle(host, channel));
        }

        if (managedDependencies)
        {
            ProjectFiles.MergeHostJson(
                Path.Combine(root, "host.json"),
                EnsureManagedDependency);
        }

        // local.settings.json
        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate(_assembly, "local.settings.json")
                .Replace(RuntimeVersionPlaceholder, runtimeVersion),
            force);

        // profile.ps1
        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "profile.ps1"),
            ProjectFiles.ReadTemplate(_assembly, "profile.ps1"),
            force);

        // .gitignore
        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate(_assembly, "gitignore"),
            force);

        // requirements.psd1 (managed dependencies only)
        if (managedDependencies)
        {
            string template = ProjectFiles.ReadTemplate(_assembly, "requirements.psd1");
            string content = await ResolveRequirementsContent(template, cancellationToken).ConfigureAwait(false);
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, "requirements.psd1"),
                content,
                force);
        }
    }

    private async Task<string> ResolveRequirementsContent(string template, CancellationToken cancellationToken)
    {
        string? majorVersion = await GetLatestAzModuleMajorVersion(cancellationToken).ConfigureAwait(false);
        if (majorVersion is not null)
        {
            return template.Replace(MajorVersionPlaceholder, majorVersion);
        }

        // Could not reach PSGallery — leave the placeholder commented out.
        return template;
    }

    private static void EnsureExtensionBundle(JsonObject host, BundleChannel channel)
    {
        if (host.ContainsKey("extensionBundle"))
        {
            return;
        }

        host["extensionBundle"] = new JsonObject
        {
            ["id"] = ExtensionBundle.IdFor(channel),
            ["version"] = ExtensionBundle.DefaultVersionRange,
        };
    }

    private static void EnsureManagedDependency(JsonObject host)
    {
        if (host.ContainsKey("managedDependency"))
        {
            return;
        }

        host["managedDependency"] = new JsonObject
        {
            ["enabled"] = true,
        };
    }

    private static async Task<string?> DefaultGetLatestAzModuleMajorVersion(CancellationToken cancellationToken)
    {
        try
        {
            Uri address = new($"{PowerShellGalleryFindPackagesByIdUri}'{AzModuleName}'");

            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "AzureFunctionsCli");
            client.Timeout = TimeSpan.FromSeconds(GalleryTimeoutSeconds);

            using HttpResponseMessage response = await client.GetAsync(address, cancellationToken).ConfigureAwait(false);
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

            XmlNode? root = doc.DocumentElement;
            XmlNodeList? props = root?.SelectNodes("//m:properties[d:IsPrerelease = \"false\"]/d:Version", nsmgr);
            var latestVersion = new Version("0.0");

            if (props is { Count: > 0 })
            {
                foreach (XmlNode prop in props)
                {
                    if (Version.TryParse(prop.FirstChild?.Value, out Version? currentVersion) && currentVersion > latestVersion)
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
            // Best-effort lookup; failures (network issues, invalid JSON, etc.) are non-fatal
            // because the Az module version is only used to pre-populate requirements.psd1.
            return null;
        }
    }
}

