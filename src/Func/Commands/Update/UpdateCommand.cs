// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Update;

/// <summary>
/// Updates the installed func CLI in place. Modeled on
/// <c>aspire update --self</c>: resolves the requested channel, downloads the
/// matching GitHub release asset for the current RID, swaps the on-disk
/// binary with backup/rollback, and verifies via <c>func --version</c>.
/// </summary>
/// <remarks>
/// Option surface is wired up now so help text, parser composition, and DI
/// registration land in one place. The download/swap/verify pipeline is added
/// in follow-up commits.
/// </remarks>
internal sealed class UpdateCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> ChannelOption { get; } = new("--channel")
    {
        Description = "Release channel to update from. One of: stable, preview. Default: stable.",
    };

    public Option<string?> VersionOption { get; } = new("--version")
    {
        Description = "Pin to a specific CLI version (e.g. 5.1.0). When set, overrides --channel selection of 'latest'.",
    };

    public Option<bool> YesOption { get; } = new("--yes", "-y")
    {
        Description = "Answer yes to confirmation prompts. Required when running non-interactively.",
    };

    public UpdateCommand()
        : base(
            "update",
            "Update the installed func CLI in place. Defaults to the latest stable release; "
            + "use '--channel preview' for the latest preview, or '--version' to pin a specific build.")
    {
        Options.Add(ChannelOption);
        Options.Add(VersionOption);
        Options.Add(YesOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        // The pipeline (channel resolution -> GitHub release lookup -> download
        // -> swap -> verify) is being added in follow-up commits. Surfacing a
        // GracefulException keeps the placeholder discoverable in --help while
        // making accidental invocations harmless and clearly self-described.
        throw new GracefulException(
            "'func update' is not yet implemented. Re-run the install script from "
            + "https://aka.ms/func-cli/install.sh or https://aka.ms/func-cli/install.ps1 to update in the meantime.",
            isUserError: true);
    }
}
