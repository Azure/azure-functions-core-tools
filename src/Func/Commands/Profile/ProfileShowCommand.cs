// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Profile;

/// <summary>
/// Shows details for one Azure Functions profile.
/// </summary>
internal sealed class ProfileShowCommand : FuncCliCommand
{
    public Argument<string> NameArgument { get; } = new("name")
    {
        Description = "The profile name to inspect."
    };

    public Option<bool> RawOption { get; } = new("--raw")
    {
        Description = "Show the raw profile definition without inherited values."
    };

    private readonly IInteractionService _interaction;
    private readonly IProfileCatalog _catalog;

    public ProfileShowCommand(IInteractionService interaction, IProfileCatalog catalog)
        : base("show", "Show details for an Azure Functions profile.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        Arguments.Add(NameArgument);
        AddPathArgument();
        Options.Add(RawOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        string name = parseResult.GetValue(NameArgument)!;
        bool raw = parseResult.GetValue(RawOption);

        try
        {
            var sourceContext = new ProfileSourceContext(workingDirectory.Info);
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _catalog.LoadAsync(sourceContext, cancellationToken);
            if (raw)
            {
                ProfileDefinitionEntry entry = _catalog.FindProfile(name, snapshots)
                    ?? throw new ProfileConfigurationException($"Profile '{name}' was not found.");
                WriteRawProfile(entry);
            }
            else
            {
                ResolvedProfile profile = _catalog.ResolveProfile(name, snapshots);
                WriteResolvedProfile(profile);
            }
        }
        catch (ProfileConfigurationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        return 0;
    }

    private void WriteResolvedProfile(ResolvedProfile profile)
    {
        List<DefinitionItem> items =
        [
            new("Name", profile.Name),
            new("Source", profile.Source.KindDisplayName),
            new("Status", profile.Status.ToString().ToLowerInvariant()),
            new("Host version", RangeText(profile.HostVersionRange)),
            new("Extension bundle", RangeText(profile.ExtensionBundleVersionRange)),
            new("Workers", FormatWorkers(profile.WorkerVersionRanges)),
            new("Supported runtimes", FormatList(profile.SupportedRuntimes, "not enforced")),
        ];

        AddIfNotEmpty(items, "SKU", profile.Sku);
        AddIfNotEmpty(items, "Deprecation URL", profile.DeprecationUrl);
        AddIfNotEmpty(items, "Notes", profile.Notes);

        _interaction.WriteDefinitionList(items);
    }

    private void WriteRawProfile(ProfileDefinitionEntry entry)
    {
        ProfileDefinition definition = entry.Definition;
        List<DefinitionItem> items =
        [
            new("Name", entry.Name),
            new("Source", entry.Source.KindDisplayName),
            new("Status", definition.Status ?? "stable"),
            new("Extends", definition.Extends ?? "-"),
            new("Host version", definition.Host?.Version ?? "-"),
            new("Extension bundle", definition.ExtensionBundle?.Version ?? "-"),
            new("Workers", FormatRawWorkers(definition.Workers)),
            new("Supported runtimes", FormatList(definition.SupportedRuntimes, "-")),
        ];

        AddIfNotEmpty(items, "SKU", definition.Sku);
        AddIfNotEmpty(items, "Deprecation URL", definition.DeprecationUrl);
        AddIfNotEmpty(items, "Notes", definition.Notes);

        _interaction.WriteDefinitionList(items);
    }

    private static void AddIfNotEmpty(List<DefinitionItem> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add(new DefinitionItem(label, value));
        }
    }

    private static string FormatWorkers(IReadOnlyDictionary<string, VersionRange> workers)
    {
        if (workers.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", workers
            .OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Select(w => $"{w.Key} {RangeText(w.Value)}"));
    }

    private static string FormatRawWorkers(IReadOnlyDictionary<string, ProfileWorkerConstraint?>? workers)
    {
        if (workers is null || workers.Count == 0)
        {
            return "-";
        }

        return string.Join(", ", workers
            .OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Select(w => w.Value is null ? $"{w.Key} <removed>" : $"{w.Key} {w.Value.Version}"));
    }

    private static string FormatList(IReadOnlyList<string>? values, string emptyText)
        => values is null || values.Count == 0
            ? emptyText
            : string.Join(", ", values);

    private static string RangeText(VersionRange? range)
        => range?.OriginalString ?? range?.ToString() ?? "-";
}
