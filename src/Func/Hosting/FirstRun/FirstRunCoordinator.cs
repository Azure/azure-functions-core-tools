// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// Default coordinator. Skips quickly when this is clearly not the
/// user's first interactive command, otherwise prompts and delegates
/// to <see cref="ISetupRunner"/>.
/// </summary>
internal sealed class FirstRunCoordinator(
    IInteractionService interaction,
    IFirstRunStateStore stateStore,
    ISetupRunner setupRunner) : IFirstRunCoordinator
{
    private const string PromptMessage = "Looks like it's your first run. Run setup now?";

    // Commands that should not trigger the first-run prompt: the user is
    // either already setting up, just inspecting the CLI, or hit a typo.
    private static readonly HashSet<string> _skippedCommandNames =
        new(StringComparer.OrdinalIgnoreCase) { "setup", "help", "version", "unknown" };

    private static readonly HashSet<string> _skippedTokens =
        new(StringComparer.OrdinalIgnoreCase) { "--help", "-h", "-?", "/?", "--version", "-v" };

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFirstRunStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly ISetupRunner _setupRunner = setupRunner ?? throw new ArgumentNullException(nameof(setupRunner));

    public async Task EnsureFirstRunPromptedAsync(string commandName, ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!_stateStore.IsFirstRun())
        {
            return;
        }

        if (!_interaction.IsInteractive)
        {
            // Don't pester the user in CI / piped scenarios, and don't mark
            // the marker either: they may run interactively next time.
            return;
        }

        if (commandName is not null && _skippedCommandNames.Contains(commandName))
        {
            return;
        }

        if (parseResult.Errors.Count > 0)
        {
            return;
        }

        foreach (System.CommandLine.Parsing.Token token in parseResult.Tokens)
        {
            if (_skippedTokens.Contains(token.Value))
            {
                return;
            }
        }

        _interaction.WriteBlankLine();
        bool runSetup = await _interaction.ConfirmAsync(PromptMessage, defaultValue: true, cancellationToken);

        if (runSetup)
        {
            await RunSetupAsync(cancellationToken);
        }
        else
        {
            _interaction.WriteHint("Skipping setup. Run `func setup` later when you're ready.");
        }

        await _stateStore.MarkCompleteAsync(cancellationToken);
    }

    private async Task RunSetupAsync(CancellationToken cancellationToken)
    {
        var options = new SetupCommandOptions(
            WorkingDirectory: new DirectoryInfo(Environment.CurrentDirectory),
            Features: [],
            ProfileNames: [],
            Source: null,
            InstallPolicy: SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: true,
            NonInteractive: false,
            AssumeYes: false,
            Check: false,
            OutputMode: SetupOutputMode.Plain);

        try
        {
            await _setupRunner.RunAsync(options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Surface the failure but don't block the user's original
            // command: they explicitly asked us to try setup, and the
            // command they typed may still succeed (or fail with its own
            // clearer error).
            _interaction.WriteWarning($"Setup did not complete: {ex.Message}");
        }
    }
}
