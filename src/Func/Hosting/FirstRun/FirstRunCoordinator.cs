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
    private const string PromptExplanation =
        "The Azure Functions CLI uses workloads (host, runtime, language stacks) to do its work, "
        + "and they aren't installed yet. Running `func setup` now will install everything you need to get started.";

    private const string PromptMessage = "Run `func setup` now?";

    // Commands that should not trigger the first-run prompt. We deliberately
    // do NOT skip on "help" / "unknown" here: those are what the resolver
    // returns for a bare `func` invocation, which is the canonical first-run
    // trigger. Explicit `--help`/`-h` invocations are handled by the token
    // check below. "version" stays because it only arises from `func --verbose`
    // with no subcommand, which is a CLI-inspection gesture, not a real
    // first command.
    private static readonly HashSet<string> _skippedCommandNames =
        new(StringComparer.OrdinalIgnoreCase) { "setup", "version" };

    private static readonly HashSet<string> _skippedTokens =
        new(StringComparer.OrdinalIgnoreCase) { "--help", "-h", "-?", "/?", "--version", "-v" };

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFirstRunStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly ISetupRunner _setupRunner = setupRunner ?? throw new ArgumentNullException(nameof(setupRunner));

    public async Task EnsureFirstRunPromptedAsync(string commandName, ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!await _stateStore.IsFirstRunAsync(cancellationToken))
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

        foreach (System.CommandLine.Parsing.Token token in parseResult.Tokens)
        {
            if (_skippedTokens.Contains(token.Value))
            {
                return;
            }
        }

        // Bare `func` (no args) produces a "Required command was not provided"
        // parse error but no tokens; that's the canonical first-run trigger
        // and we still want to prompt. For any other parse error (typo, bad
        // option), stay quiet until the user fixes their command line.
        bool isBareInvocation = parseResult.Tokens.Count == 0;
        if (!isBareInvocation && parseResult.Errors.Count > 0)
        {
            return;
        }

        _interaction.WriteBlankLine();
        _interaction.WriteHint(PromptExplanation);
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
