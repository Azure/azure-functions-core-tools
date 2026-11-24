// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupFeatureResolver
{
    /// <summary>
    /// Resolves setup features before any installation. Returns null when the picker
    /// has no remaining stacks with a setup recipe.
    /// </summary>
    public Task<SetupFeaturePlan?> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken);
}

internal sealed class SetupFeatureResolver(
    IInteractionService interaction, StackPackageCatalog packages, IProjectStackConfiguration configuration) : ISetupFeatureResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly StackPackageCatalog _packages = packages ?? throw new ArgumentNullException(nameof(packages));
    private readonly IProjectStackConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async Task<SetupFeaturePlan?> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Features.Count > 0)
        {
            string[] requested = [.. options.Features.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase)];
            string[] names = [.. requested.Where(f => !IsCoreFeature(f))];
            IReadOnlyList<StackPackage> packages = names.Length == 0 ? [] : await GetPackagesAsync(names, options, byRuntime: false, cancellationToken);
            return BuildPlan(requested, packages, allowWorkerOnly: true);
        }

        ProjectStackSettings settings = _configuration.Read(options.WorkingDirectory);
        string? configured = SetupRuntimes.NullIfWhiteSpace(settings.StackIdentifier);
        string? hostRuntime = settings.HasProjectConfig ? null : SetupRuntimes.NullIfWhiteSpace(settings.WorkerRuntime);
        if ((configured ?? hostRuntime) is { } value)
        {
            bool byRuntime = configured is null;
            IReadOnlyList<StackPackage> packages = await GetPackagesAsync([value], options, byRuntime, cancellationToken);
            string source = byRuntime ? ProjectStackSettings.WorkerRuntimeSource : ProjectStackSettings.StackSource;
            StackPackage selected = FindUnique(packages, value, byRuntime)
                ?? throw new SetupConfigurationException($"No stack matches {source} '{value}'. Run 'func workload search --stack' to discover stacks.");
            return BuildPlan([selected.Stack.Identifier], packages, allowWorkerOnly: false);
        }

        if (!options.NonInteractive && !options.AssumeYes && options.OutputMode != SetupOutputMode.Json && _interaction.IsInteractive)
        {
            InstalledStackInventory installed = await _packages.GetInstalledAsync(cancellationToken);
            IReadOnlyList<StackPackage> inventory = await _packages.DiscoverAsync(installed.Stacks, options.Source, options.IncludePrerelease, cancellationToken);
            IReadOnlyList<string>? picked = await PromptAsync(inventory, cancellationToken);
            return picked is null ? null : BuildPlan(picked, inventory, allowWorkerOnly: false);
        }

        if (options.AssumeYes && options.OutputMode == SetupOutputMode.Plain)
        {
            _interaction.WriteHint(
                "No language stack was selected. Setup targets only the host and extension bundle. "
                + "For language-specific setup, run `func setup --yes --features <stack>`.");
        }

        return BuildPlan(["runtime"], [], allowWorkerOnly: false);
    }

    private async Task<IReadOnlyList<StackPackage>> GetPackagesAsync(
        IReadOnlyList<string> names, SetupCommandOptions options, bool byRuntime, CancellationToken cancellationToken)
    {
        InstalledStackInventory installed = await _packages.GetInstalledAsync(cancellationToken);
        string[] missing = [.. names.Where(name => FindUnique(installed.Stacks, name, byRuntime) is null)];
        if (missing.Length == 0)
        {
            return installed.Stacks;
        }

        try
        {
            return await _packages.DiscoverAsync(installed.Stacks, options.Source, options.IncludePrerelease, cancellationToken);
        }
        catch (WorkloadCatalogException ex) when (options.Features.Count > 0 && missing.All(name =>
            !SetupDependency.SupportsStack(name) && installed.HasPackage(SetupDependency.Worker(name, null).PackageId, options.IncludePrerelease)))
        {
            // Check discovery first: a published stack takes precedence over a similarly
            // named standalone worker. If offline, only fall back to actual installed workers.
            new SetupRenderer(_interaction, options.OutputMode).Warning($"Stack discovery is unavailable; using installed workload metadata: {ex.Message}");
            return installed.Stacks;
        }
    }

    private async Task<IReadOnlyList<string>?> PromptAsync(IReadOnlyList<StackPackage> inventory, CancellationToken cancellationToken)
    {
        List<MultiSelectionChoice> choices = [];
        List<string> installed = [];
        foreach (IGrouping<string, StackPackage> owners in inventory.GroupBy(p => p.Stack.Identifier, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            StackPackage package = RequireSingleOwner(owners, owners.Key);
            string label = $"{package.Stack.DisplayName} ({package.Stack.Identifier})";
            if (package.Installed)
            {
                installed.Add(label);
            }
            else if (SetupDependency.SupportsStack(package.Stack.Identifier))
            {
                choices.Add(new MultiSelectionChoice(package.Stack.Identifier, label));
            }
            else
            {
                _interaction.WriteHint(NoRecipeMessage(package));
            }
        }

        if (installed.Count > 0)
        {
            _interaction.WriteBlankLine();
            _interaction.WriteLine(l => l.Muted("Already installed (use `func workload uninstall <name>` to remove):"));
            foreach (string label in installed)
            {
                _interaction.WriteLine(l => l.Muted($"   [✓] {label}"));
            }

            _interaction.WriteBlankLine();
        }

        if (choices.Count == 0)
        {
            if (installed.Count == 0)
            {
                throw new SetupConfigurationException("No stacks with a setup recipe were found. Run 'func workload search --stack' to find individual packages.");
            }

            return null;
        }

        return await _interaction.PromptForMultiSelectionAsync(
            "Select stacks to install (SPACE to toggle, ENTER to confirm; CTRL+C to cancel):", choices, cancellationToken);
    }

    private static SetupFeaturePlan BuildPlan(IReadOnlyList<string> requested, IReadOnlyList<StackPackage> packages, bool allowWorkerOnly)
    {
        if (requested.Count == 0)
        {
            throw new SetupConfigurationException("At least one setup feature is required.");
        }

        List<string> features = [];
        List<SetupRuntimeFeature> runtimes = [];
        bool includeBundle = false;
        foreach (string name in requested)
        {
            if (IsCoreFeature(name))
            {
                features.Add(name.ToLowerInvariant());
                includeBundle |= string.Equals(name, "runtime", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            StackPackage? package = FindUnique(packages, name, byRuntime: false);
            if (package is null)
            {
                if (!allowWorkerOnly || SetupDependency.SupportsStack(name)
                    || packages.Any(p => string.Equals(p.Stack.WorkerRuntime, name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new SetupConfigurationException($"No stack has the ID '{name}'. Use a stack ID from 'func workload search --stack'.");
                }

                // This path is intentionally available for standalone workers that do
                // not declare a project stack. The installer requires their worker package.
                string worker = name.ToLowerInvariant();
                features.Add(worker);
                runtimes.Add(new SetupRuntimeFeature(worker, worker, InstallWorker: true));
                includeBundle = true;
                continue;
            }

            SetupRuntimeFeature recipe = SetupFeatureCatalog.GetStackRecipe(package) ?? throw new SetupConfigurationException(NoRecipeMessage(package));
            features.Add(recipe.Name);
            runtimes.Add(recipe);
            includeBundle |= recipe.InstallWorker;
        }

        return new SetupFeaturePlan(features, runtimes,
            [.. runtimes.Where(r => r.InstallWorker).Select(r => r.Name).Order(StringComparer.OrdinalIgnoreCase)], includeBundle);
    }

    private static StackPackage? FindUnique(IReadOnlyList<StackPackage> packages, string value, bool byRuntime)
    {
        StackPackage[] matches = [.. packages.Where(p => string.Equals(byRuntime ? p.Stack.WorkerRuntime : p.Stack.Identifier,
            value, StringComparison.OrdinalIgnoreCase))];
        return matches.Length == 0 ? null : RequireSingleOwner(matches, value);
    }

    private static StackPackage RequireSingleOwner(IEnumerable<StackPackage> packages, string value)
    {
        StackPackage[] matches = [.. packages];
        return matches.Length == 1 ? matches[0]
            : throw new SetupConfigurationException($"'{value}' is ambiguous. Matching stack packages: {string.Join(", ", matches.Select(p => $"{p.PackageId} ({p.Stack.Identifier})"))}.");
    }

    private static string NoRecipeMessage(StackPackage package)
        => $"No full setup recipe is available for stack '{package.Stack.Identifier}'. Install its workload with 'func workload install {package.PackageId} --exact --version {package.Version.ToNormalizedString()}'.";

    private static bool IsCoreFeature(string name)
        => string.Equals(name, "host", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "runtime", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string feature)
    {
        string value = SetupRuntimes.NullIfWhiteSpace(feature) ?? throw new SetupConfigurationException("Setup feature names cannot be empty.");
        if (SetupFeatureCatalog.IsUnsupportedLegacyFeature(value))
        {
            throw new SetupConfigurationException($"The '{value}' feature is not supported. Use a stack ID from 'func workload search --stack'.");
        }

        return value;
    }
}
