// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupFeatureResolver
{
    /// <summary>
    /// Resolves the requested feature list into a plan, prompting for stacks when
    /// none were supplied and the shell is interactive.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the interactive prompt had nothing to offer
    /// because every supported stack is already installed. Callers treat that as
    /// a clean no-op rather than a failure.
    /// </returns>
    public Task<SetupFeaturePlan?> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken);
}

internal sealed class SetupFeatureResolver(
    IInteractionService interaction,
    IWorkloadStore workloadStore,
    ICliConfigurationProvider configurationProvider,
    ISetupStackCatalog stackCatalog) : ISetupFeatureResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly ICliConfigurationProvider _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    private readonly ISetupStackCatalog _stackCatalog = stackCatalog ?? throw new ArgumentNullException(nameof(stackCatalog));

    public async Task<SetupFeaturePlan?> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<string>? requestedFeatures = options.Features.Count == 0
            ? await GetDefaultFeaturesAsync(options, cancellationToken)
            : options.Features;

        if (requestedFeatures is null)
        {
            // Interactive prompt had nothing to offer (every supported stack
            // is already installed); the caller treats this as a graceful exit.
            return null;
        }

        if (requestedFeatures.Count == 0)
        {
            throw new SetupConfigurationException("At least one setup feature is required.");
        }

        List<string> features = [];
        HashSet<string> featureNames = new(StringComparer.OrdinalIgnoreCase);
        List<SetupRuntimeFeature> runtimeFeatures = [];
        HashSet<string> runtimeFeatureNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> workerRuntimes = new(StringComparer.OrdinalIgnoreCase);
        bool includeExtensionBundle = false;

        // Fetched on the first stack-shaped feature so a host-only run still
        // makes no catalog call. Results are cached, so the plan builder's
        // later lookup costs nothing.
        SetupStackSnapshot? stacks = null;

        foreach (string rawFeature in requestedFeatures)
        {
            string feature = NormalizeFeature(rawFeature);

            // Fold before dispatch, not inside the default arm. A stack with
            // dedicated handling has to reach its own case: an alternate
            // spelling of dotnet folded afterwards would already have been
            // routed to the generic path and picked up a worker and a bundle
            // that dotnet doesn't use.
            if (!IsResolverKeyword(feature))
            {
                stacks ??= await _stackCatalog.GetStacksAsync(options.Source, options.IncludePrerelease, cancellationToken);
                feature = stacks.CanonicalStackName(feature);
            }

            switch (feature)
            {
                case "host":
                    AddFeature(features, featureNames, "host");
                    break;

                case "runtime":
                    if (AddFeature(features, featureNames, "runtime"))
                    {
                        includeExtensionBundle = true;
                    }

                    break;

                case SetupRuntimes.DotNetFeature:
                case SetupRuntimes.DotNetProfileRuntime:
                    if (AddFeature(features, featureNames, SetupRuntimes.DotNetFeature))
                    {
                        AddRuntimeFeature(runtimeFeatures, runtimeFeatureNames, SetupRuntimes.DotNetFeature, SetupRuntimes.DotNetProfileRuntime, installWorker: false);
                    }

                    break;

                case ".net":
                    throw new SetupConfigurationException($"The '{rawFeature}' feature is not supported. Use 'dotnet'.");

                case "dotnet-inprocess":
                    throw new SetupConfigurationException($"The '{rawFeature}' feature is not supported. Use 'dotnet'.");

                default:
                    if (!AddFeature(features, featureNames, feature))
                    {
                        break;
                    }

                    AddRuntimeFeature(runtimeFeatures, runtimeFeatureNames, feature, profileRuntime: feature, installWorker: true);
                    workerRuntimes.Add(feature);
                    if (SetupRuntimes.GetBundlePolicy(feature) == SetupBundlePolicy.DefaultStable)
                    {
                        includeExtensionBundle = true;
                    }

                    break;
            }
        }

        return new SetupFeaturePlan(
            [.. features],
            [.. runtimeFeatures],
            [.. workerRuntimes.OrderBy(static runtime => runtime, StringComparer.OrdinalIgnoreCase)],
            includeExtensionBundle);
    }

    private async Task<IReadOnlyList<string>?> GetDefaultFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        string? configuredStack = _configurationProvider
            .GetProjectConfiguration(options.WorkingDirectory)
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"];

        if (!string.IsNullOrWhiteSpace(configuredStack))
        {
            return [configuredStack.Trim()];
        }

        if (!options.NonInteractive && _interaction.IsInteractive)
        {
            StackChoicesResult choices = await BuildStackChoicesAsync(options, cancellationToken);

            // Render installed stacks as static "fake checkbox" lines above
            // the prompt so they're visible in context but cannot be toggled
            // (Spectre's MultiSelectionPrompt has no read-only items, and a
            // toggle visually implies an uninstall that `func setup` doesn't do).
            if (choices.InstalledStacks.Count > 0)
            {
                _interaction.WriteBlankLine();
                _interaction.WriteLine(l => l.Muted("Already installed (use `func workload uninstall <name>` to remove):"));
                foreach (string stack in choices.InstalledStacks)
                {
                    _interaction.WriteLine(l => l.Muted($"   [✓] {stack}"));
                }

                _interaction.WriteBlankLine();
            }

            if (choices.PromptChoices.Count == 0)
            {
                // Every supported stack is already installed; nothing to
                // offer. Treat as a clean opt-out so the caller marks the
                // first-run flag and exits without prompting.
                return null;
            }

            IReadOnlyList<string> picked = await _interaction.PromptForMultiSelectionAsync(
                "Select stacks to install (SPACE to toggle, ENTER to confirm; CTRL+C to cancel):",
                choices.PromptChoices,
                cancellationToken);

            return picked;
        }

        return ["runtime"];
    }

    private async Task<StackChoicesResult> BuildStackChoicesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        SetupStackSnapshot snapshot = await _stackCatalog.GetStacksAsync(options.Source, options.IncludePrerelease, cancellationToken);

        // A stack aliased as one of the CLI's own feature words can't be
        // offered: picking it comes back through as that word, dispatches to
        // the built-in arm, and the package the user chose is never planned.
        IReadOnlyList<string> stacks = [.. snapshot.StackNames.Where(static stack => !IsResolverKeyword(stack))];
        HashSet<string> installedStackPackageIds;
        try
        {
            IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
            installedStackPackageIds = new HashSet<string>(
                installed.Select(static entry => entry.PackageId),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Surfacing installed stacks is a UX hint, not a contract. If we
            // can't read the store, fall back to showing every stack as
            // available so the user can still make a selection.
            installedStackPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        List<MultiSelectionChoice> promptChoices = [];
        List<string> installedStacks = [];
        foreach (string stack in stacks.OrderBy(static stack => stack, StringComparer.OrdinalIgnoreCase))
        {
            if (snapshot.StackPackageId(stack) is { } packageId && installedStackPackageIds.Contains(packageId))
            {
                installedStacks.Add(stack);
            }
            else
            {
                promptChoices.Add(new MultiSelectionChoice(stack, stack));
            }
        }

        return new StackChoicesResult(promptChoices, installedStacks);
    }

    private static bool AddFeature(List<string> features, HashSet<string> featureNames, string feature)
    {
        if (!featureNames.Add(feature))
        {
            return false;
        }

        features.Add(feature);
        return true;
    }

    private static void AddRuntimeFeature(
        List<SetupRuntimeFeature> runtimeFeatures,
        HashSet<string> runtimeFeatureNames,
        string name,
        string profileRuntime,
        bool installWorker)
    {
        if (runtimeFeatureNames.Add(name))
        {
            runtimeFeatures.Add(new SetupRuntimeFeature(name, profileRuntime, installWorker));
        }
    }

    /// <summary>
    /// Feature names the switch below dispatches on directly rather than
    /// treating as stack aliases. Two consequences: folding them is a no-op and
    /// would put a host-only run on the network, and a discovered stack under
    /// one of these names can't be offered, because selecting it lands on the
    /// built-in arm and the package is never planned.
    /// </summary>
    /// <remarks>
    /// <c>dotnet</c> is deliberately absent. It reaches the built-in arm too,
    /// but that arm keeps the name, so a discovered dotnet stack still resolves
    /// and is safe to offer.
    /// </remarks>
    internal static readonly FrozenSet<string> ResolverKeywords =
        new[] { "host", "runtime", ".net", "dotnet-inprocess", SetupRuntimes.DotNetProfileRuntime }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsResolverKeyword(string feature) => ResolverKeywords.Contains(feature);

    private static string NormalizeFeature(string value)
    {
        string? normalized = SetupRuntimes.NullIfWhiteSpace(value);
        if (normalized is null)
        {
            throw new SetupConfigurationException("Setup feature names cannot be empty.");
        }

        return normalized.ToLowerInvariant();
    }

    private readonly record struct StackChoicesResult(
        IReadOnlyList<MultiSelectionChoice> PromptChoices,
        IReadOnlyList<string> InstalledStacks);
}
