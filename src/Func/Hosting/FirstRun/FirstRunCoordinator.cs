// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// Default coordinator. Skips quickly when this is clearly not the
/// user's first interactive command, otherwise prompts and delegates
/// to <see cref="ISetupRunner"/>. Once setup has been handled, also
/// surfaces a muted breadcrumb when the user previously declined but
/// still has no workloads installed.
/// </summary>
internal sealed class FirstRunCoordinator(
    IInteractionService interaction,
    IFirstRunStateStore stateStore,
    ISetupRunner setupRunner) : IFirstRunCoordinator
{
    private const string PromptParagraph1 =
        "Looks like this is your first time running func.";

    private const string PromptParagraph2 =
        "The new CLI uses installable workloads to bring in language stacks "
        + "(Node.js, Python, .NET, Go) and the host runtime. Running `func setup` "
        + "now will install the host and the dev environment / dependencies for "
        + "the stack(s) you pick.";

    private const string PromptParagraph3 =
        "You can always add more later with `func setup --features <node|dotnet|go|python>`.";

    private const string PromptMessage = "Run `func setup` now?";

    private const string BreadcrumbHint =
        "Tip: run `func setup` to install your dev environment and language stack dependencies.";

    // Top-level command segments that should not trigger the first-run prompt
    // or breadcrumb. We deliberately do NOT skip on "help" / "unknown" here:
    // those are what the resolver returns for a bare `func` invocation, which
    // is the canonical first-run trigger. Explicit `--help`/`-h` invocations
    // are handled by the token check below. "version" stays because it only
    // arises from `func --verbose` with no subcommand, which is a CLI-
    // inspection gesture, not a real first command. "workload" covers every
    // `func workload ...` subcommand: a user reaching for those is already
    // managing their setup directly. `init` and `new` stay off the skip list
    // intentionally; the spec wants the prompt there too.
    private static readonly HashSet<string> _skippedRootCommands =
        new(StringComparer.OrdinalIgnoreCase) { "setup", "version", "workload" };

    private static readonly HashSet<string> _skippedTokens =
        new(StringComparer.OrdinalIgnoreCase) { "--help", "-h", "-?", "/?", "--version", "-v" };

    // CommandNameResolver returns these when the user didn't actually type a
    // command name: "help" for bare `func`, "version" for `func --verbose`,
    // "unknown" when the parse failed. We still want to prompt + run setup in
    // those cases, but the post-setup "Re-run `func <name>`" hint doesn't make
    // sense (re-run what?), so we short-circuit with a generic message instead.
    private static readonly HashSet<string> _commandNameSentinels =
        new(StringComparer.OrdinalIgnoreCase) { "help", "version", "unknown" };

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFirstRunStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly ISetupRunner _setupRunner = setupRunner ?? throw new ArgumentNullException(nameof(setupRunner));

    public async Task<int?> EnsureFirstRunPromptedAsync(string commandName, ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!_interaction.IsInteractive)
        {
            // Don't pester the user in CI / piped scenarios, and don't mark
            // the marker either: they may run interactively next time.
            return null;
        }

        if (IsSkipped(commandName, parseResult))
        {
            return null;
        }

        FirstRunState state = await _stateStore.GetStateAsync(cancellationToken);

        switch (state)
        {
            case FirstRunState.WorkloadsInstalled:
                return null;

            case FirstRunState.MarkerWithoutWorkloads:
                _interaction.WriteHint(BreadcrumbHint);
                return null;

            case FirstRunState.NeverPrompted:
                return await HandleFirstRunAsync(commandName, cancellationToken);

            default:
                return null;
        }
    }

    private static bool IsSkipped(string commandName, ParseResult parseResult)
    {
        if (!string.IsNullOrEmpty(commandName))
        {
            int spaceIndex = commandName.IndexOf(' ');
            string rootSegment = spaceIndex < 0 ? commandName : commandName[..spaceIndex];
            if (_skippedRootCommands.Contains(rootSegment))
            {
                return true;
            }
        }

        foreach (System.CommandLine.Parsing.Token token in parseResult.Tokens)
        {
            if (_skippedTokens.Contains(token.Value))
            {
                return true;
            }
        }

        // Bare `func` (no tokens) is the canonical first-run trigger and
        // must always be considered, even if the parser produced a
        // "Required command was not provided" error. For any other parse
        // error (typo, bad option), stay quiet until the user fixes their
        // command line.
        bool isBareInvocation = parseResult.Tokens.Count == 0;
        if (!isBareInvocation && parseResult.Errors.Count > 0)
        {
            return true;
        }

        return false;
    }

    private async Task<int?> HandleFirstRunAsync(string commandName, CancellationToken cancellationToken)
    {
        _interaction.WriteBlankLine();
        _interaction.WriteLine(PromptParagraph1);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(PromptParagraph2);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(PromptParagraph3);
        _interaction.WriteBlankLine();

        bool runSetup;
        try
        {
            runSetup = await _interaction.ConfirmAsync(PromptMessage, defaultValue: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C at the prompt = "stop pestering me". Write the marker
            // so we don't ask again next time, then propagate the cancel.
            // Use None: the inbound token is already cancelled, and the
            // intent here is precisely to persist *despite* cancellation.
            await TryMarkCompleteAsync(CancellationToken.None);
            throw;
        }

        bool setupRan = false;
        if (runSetup)
        {
            setupRan = await RunSetupAsync(cancellationToken);
        }
        else
        {
            _interaction.WriteHint("Skipping setup. Run `func setup` later when you're ready.");
        }

        await _stateStore.MarkCompleteAsync(cancellationToken);

        // After a successful setup we always short-circuit the user's original
        // command. The workload loader snapshots templates/bundles/host metadata
        // at host build time, so anything the user typed (init, new, run,
        // start, quickstart, ...) is operating against the pre-install view
        // and would either fail outright ("no project here") or silently use
        // stale metadata. Asking them to re-run is the only safe answer until
        // we add an in-process reload.
        //
        // "help" (bare `func`), "version" (`func --verbose`), and "unknown"
        // (parse error) aren't real commands the user typed, so the generic
        // "setup complete" line is enough; we don't want to print
        // "Re-run `func help`".
        if (setupRan)
        {
            _interaction.WriteBlankLine();
            if (string.IsNullOrEmpty(commandName) || _commandNameSentinels.Contains(commandName))
            {
                _interaction.WriteHint("Setup complete. Run `func <command>` to use the new stacks.");
            }
            else
            {
                _interaction.WriteHint($"Setup complete. Re-run `func {commandName.ToLowerInvariant()}` to use the new stacks.");
            }

            return 0;
        }

        return null;
    }

    private async Task<bool> RunSetupAsync(CancellationToken cancellationToken)
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
            SetupRunResult result = await _setupRunner.RunAsync(options, cancellationToken);
            return result.ExitCode == 0;
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
            return false;
        }
    }

    private async Task TryMarkCompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _stateStore.MarkCompleteAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The user already cancelled; let the outer catch propagate
            // the original OCE.
        }
        catch (Exception)
        {
            // Failing to mark on a cancel path is a minor nuisance (one
            // extra prompt next time), not worth surfacing on top of the
            // cancellation the user just triggered.
        }
    }
}
