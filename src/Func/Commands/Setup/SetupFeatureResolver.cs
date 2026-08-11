// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    ICliConfigurationProvider configurationProvider) : ISetupFeatureResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly ICliConfigurationProvider _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));

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

        foreach (string rawFeature in requestedFeatures)
        {
            string feature = NormalizeFeature(rawFeature);

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
            StackChoicesResult choices = await BuildStackChoicesAsync(cancellationToken);

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

    private async Task<StackChoicesResult> BuildStackChoicesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> stacks = SetupDependency.Stacks;
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
            if (installedStackPackageIds.Contains(SetupDependency.Stack(stack).PackageId))
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
