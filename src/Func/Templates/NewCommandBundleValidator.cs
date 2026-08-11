// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandBundleValidator
{
    public Task<int> ValidateAsync(NewCommandResolvedContext context, CancellationToken cancellationToken);
}

internal sealed class NewCommandBundleValidator(
    IInteractionService interaction,
    IHostJsonBundleSectionReader hostJsonReader,
    IExtensionBundleResolver bundleResolver,
    ITemplatesWorkloadManifestReader manifestReader) : INewCommandBundleValidator
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IHostJsonBundleSectionReader _hostJsonReader = hostJsonReader ?? throw new ArgumentNullException(nameof(hostJsonReader));
    private readonly IExtensionBundleResolver _bundleResolver = bundleResolver ?? throw new ArgumentNullException(nameof(bundleResolver));
    private readonly ITemplatesWorkloadManifestReader _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));

    public async Task<int> ValidateAsync(NewCommandResolvedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
            || context.BundleId is null)
        {
            return 0;
        }

        HostJsonBundleSection? section = await _hostJsonReader.ReadAsync(context.WorkingDirectory.Info, cancellationToken);
        if (section is null)
        {
            return 0;
        }

        var projectContext = new ExtensionBundleProjectContext(
            BundleId: section.Id,
            HostJsonVersionRange: section.Version,
            WorkerRuntime: context.Stack,
            ProfileName: null,
            ProfileBundleVersionRange: null);

        ExtensionBundleResolution resolution = await _bundleResolver.ResolveAsync(projectContext, cancellationToken);
        switch (resolution)
        {
            case ExtensionBundleResolution.Resolved bundleResolved:
                string? minRange = _manifestReader.GetMinBundleVersion(context.Workload.InstallDirectory);
                if (!string.IsNullOrWhiteSpace(minRange)
                    && !VersionRangeContains(minRange, bundleResolved.Version))
                {
                    _interaction.WriteError(
                        $"Installed templates workload '{context.Workload.PackageVersion}' requires " +
                        $"extension bundle in range '{minRange}', but the project resolves to '{bundleResolved.Version}'.");
                    _interaction.WriteLine(line => line
                        .Muted("Update the bundle range in ")
                        .Code("host.json")
                        .Muted(" or install an older templates workload pkg version."));
                    return 1;
                }

                return 0;

            case ExtensionBundleResolution.WorkloadMissing:
            case ExtensionBundleResolution.EmptyIntersection:
                _interaction.WriteError("The project requires an extension bundle but none is resolvable.");
                _interaction.WriteLine(line => line
                    .Muted("Install one with: ")
                    .Code("func workload install Azure.Functions.Cli.Workloads.ExtensionBundles")
                    .Muted(" or run ")
                    .Code("func setup")
                    .Muted("."));
                return 1;

            default:
                return 0;
        }
    }

    internal static bool VersionRangeContains(string range, string version)
    {
        if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        string trimmed = range.Trim();
        string lowerBound = trimmed;
        if (trimmed.StartsWith('[') || trimmed.StartsWith('('))
        {
            int comma = trimmed.IndexOf(',');
            lowerBound = comma > 1
                ? trimmed[1..comma].Trim()
                : trimmed.Trim('[', '(', ']', ')').Trim();
        }

        return CompareVersions(version, lowerBound) >= 0;
    }

    private static int CompareVersions(string first, string second)
    {
        if (Version.TryParse(StripPrerelease(first), out Version? firstVersion)
            && Version.TryParse(StripPrerelease(second), out Version? secondVersion))
        {
            return firstVersion.CompareTo(secondVersion);
        }

        return string.Compare(first, second, StringComparison.Ordinal);
    }

    private static string StripPrerelease(string version)
    {
        int dash = version.IndexOf('-');
        return dash < 0 ? version : version[..dash];
    }
}