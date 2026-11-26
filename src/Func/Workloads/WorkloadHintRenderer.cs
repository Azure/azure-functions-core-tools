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
        _interaction.WriteWarning($"No installed stack is available to {hint.ActionDescription}.");
        WriteDiscoveryHint();
    }

    private void RenderNoMatchingStack(WorkloadHint hint)
    {
        _interaction.WriteError($"No installed stack matches --stack '{hint.RequestedStack}'.");
        if (hint.InstalledStacks.Count > 0)
        {
            WriteInstalledStacks(hint.InstalledStacks);
        }

        WriteDiscoveryHint();
    }

    private void RenderAmbiguousStackChoice(WorkloadHint hint)
    {
        _interaction.WriteHint("Multiple stacks installed; pass --stack <name> to choose.");
        WriteInstalledStacks(hint.InstalledStacks);
    }

    private void RenderAutoSelectedSoleWorkload(WorkloadHint hint)
    {
        _interaction.WriteHint(
            $"Auto-selecting '{hint.RequestedStack}' (the only installed stack available to {hint.ActionDescription}).");
        _interaction.WriteLine(l => l
            .Muted("Install more with ")
            .Command("func setup")
            .Muted(" or run ")
            .Command("func workload search --stack")
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

    private void WriteDiscoveryHint()
    {
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Command("func workload search --stack")
            .Muted(" to find stack packages. Install one with ")
            .Command("func workload install <package-id> --exact")
            .Muted("."));
    }
}
