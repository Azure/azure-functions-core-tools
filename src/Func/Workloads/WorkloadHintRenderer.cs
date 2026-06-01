// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default <see cref="IWorkloadHintRenderer"/>. Owns the wording and layout
/// of every workload-availability message; commands describe the situation
/// via <see cref="WorkloadHint"/> and never touch <c>IInteractionService</c>
/// for these flows directly. Internal — workload authors don't see this.
/// </summary>
internal sealed class WorkloadHintRenderer(IInteractionService interaction) : IWorkloadHintRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public void Render(WorkloadHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);

        switch (hint.Kind)
        {
            case WorkloadHintKind.NoWorkloadsInstalled:
                RenderNoWorkloadsInstalled(hint);
                break;
            case WorkloadHintKind.NoMatchingStack:
                RenderNoMatchingStack(hint);
                break;
            case WorkloadHintKind.AmbiguousStackChoice:
                RenderAmbiguousStackChoice(hint);
                break;
            case WorkloadHintKind.AutoSelectedSoleWorkload:
                RenderAutoSelectedSoleWorkload(hint);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hint), hint.Kind, "Unknown workload hint kind.");
        }
    }

    private void RenderNoWorkloadsInstalled(WorkloadHint hint)
    {
        _interaction.WriteWarning("No stacks installed.");
        _interaction.WriteBlankLine();
        _interaction.WriteHint($"Set up a stack to {hint.ActionDescription}:");
        _interaction.WriteBlankLine();

        IEnumerable<DefinitionItem> items = KnownInstallableWorkloads.LanguageMap.Select(static kvp =>
            new DefinitionItem($"func setup --features {kvp.Key}", string.Join(", ", kvp.Value)));
        _interaction.WriteDefinitionList(items);

        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Command("func workload search")
            .Muted(" to discover available stacks."));
    }

    private void RenderNoMatchingStack(WorkloadHint hint)
    {
        _interaction.WriteError($"No installed stack matches '{hint.RequestedStack}'.");
        WriteInstalledStacks(hint.InstalledStacks);
    }

    private void RenderAmbiguousStackChoice(WorkloadHint hint)
    {
        _interaction.WriteHint("Multiple stacks installed; pass --stack <name> to choose.");
        WriteInstalledStacks(hint.InstalledStacks);
    }

    private void RenderAutoSelectedSoleWorkload(WorkloadHint hint)
    {
        _interaction.WriteHint(
            $"Auto-selecting '{hint.RequestedStack}' (the only installed workload).");
        _interaction.WriteLine(l => l
            .Muted("Install more with ")
            .Command("func setup")
            .Muted(" or run ")
            .Command("func workload search")
            .Muted(" to discover available stacks."));
    }

    private void WriteInstalledStacks(IReadOnlyList<string> stacks)
    {
        _interaction.WriteBlankLine();
        _interaction.WriteHint("Installed stacks:");
        foreach (string stack in stacks)
        {
            _interaction.WriteLine(l => l
                .Muted("  ")
                .Code(stack));
        }
    }
}
