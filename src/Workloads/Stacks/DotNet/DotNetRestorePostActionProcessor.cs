// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Restores generated .NET files after template creation.
/// </summary>
internal sealed class DotNetRestorePostActionProcessor(IDotnetCliRunner dotnetCli, DotNetProjectStack stack)
    : RestorePostAction
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly DotNetProjectStack _stack = stack ?? throw new ArgumentNullException(nameof(stack));

    /// <inheritdoc />
    public override bool CanProcess(PostActionSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsCompatibleWith(_stack);
    }

    /// <inheritdoc />
    protected override Task<bool> RestoreAsync(
        PostActionContext context, string targetPath, CancellationToken cancellationToken)
        => _dotnetCli.TryRunPostActionAsync(
            context, ["restore", targetPath, "-tlp:verbosity=quiet", "--no-logo"], "dotnet restore", cancellationToken);
}
