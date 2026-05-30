// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Centralised user-facing strings for quickstart commands.
/// Keeps message text discoverable and consistent across subcommands.
/// </summary>
internal static class QuickstartMessages
{
    internal const string SuccessIcon = "✓ ";
    internal const string StepBullet = "  · ";

    // Shared across commands
    internal const string FetchingCatalogStatus = "Fetching template catalog...";
    internal const string TemplateNotFoundHint = "Run `func quickstart list` to see available templates.";
    internal const string HelpFooterHint = "Looking for more stacks? Run `func workload search --stack` to list installable stack workloads.";

    // Builds the help text for `--stack`. Stacks come from installed quickstart
    // providers' Stack ids, lowercased and sorted for stable presentation.
    // Matching in the resolver is case-insensitive, so we surface the canonical
    // lowercase form.
    internal static string BuildStackOptionDescription(IReadOnlyList<IQuickstartProvider> providers)
    {
        var stacks = providers
            .Select(p => p.Stack)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return stacks.Count == 0
            ? "The stack to use. Set up a stack (`func setup --features <id>`) to see supported values."
            : "The stack to use. Supported values: " + string.Join(", ", stacks) + ".";
    }

    // QuickstartCommand
    internal const string DirectoryNotEmptyError = "The target directory is not empty. Pass --force to overwrite, or choose a different path.";
    internal const string CancelledHint = "Quickstart cancelled. The directory was not modified.";
    internal const string NoMatchingFiltersError = "No templates match the specified filters. Run `func quickstart list` to see all available templates, or adjust --resource, --iac, or --search.";
    internal const string MultipleMatchesError = "Multiple templates match. Re-run with --template <id> to select one, or add filters (--resource, --iac, --search) to narrow the results.";

    // QuickstartListCommand
    internal const string NoMatchingFiltersWarning = "No templates match the specified filters.";

    // QuickstartInfoCommand
    internal const string WhatsIncludedHeading = "What's included:";
}
