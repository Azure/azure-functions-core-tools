// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Update;
using Semver;

namespace Azure.Functions.Cli.Commands.Update;

/// <summary>
/// Updates the installed func CLI in place: resolves the requested release
/// (latest stable, latest prerelease, or a pinned version), compares against
/// the running CLI's version, and delegates to <see cref="ICliUpdater"/> for
/// the download/swap/verify pipeline. Refuses to run when the installation
/// is owned by a package manager (npm/brew/choco/winget) and instead points
/// the user at the correct upgrade command.
/// </summary>
internal sealed class UpdateCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<bool> PrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Update to the latest release including prereleases. Default: stable releases only.",
    };

    public Option<string?> VersionOption { get; } = new("--version")
    {
        Description = "Pin to a specific CLI version (e.g. 5.1.0). When set, '--prerelease' is still allowed for clarity.",
    };

    public Option<bool> YesOption { get; } = new("--yes", "-y")
    {
        Description = "Answer yes to confirmation prompts. Required when running non-interactively.",
    };

    private readonly IReleaseFeed _releaseFeed;
    private readonly ICliUpdater _updater;
    private readonly IInstallMethodDetector _installMethodDetector;
    private readonly ICliVersionProvider _versionProvider;
    private readonly IInteractionService _interaction;

    public UpdateCommand(
        IReleaseFeed releaseFeed,
        ICliUpdater updater,
        IInstallMethodDetector installMethodDetector,
        ICliVersionProvider versionProvider,
        IInteractionService interaction)
        : base(
            "update",
            "Update the installed func CLI in place. Defaults to the latest stable release; "
            + "use '--prerelease' for the latest prerelease, or '--version' to pin a specific build.")
    {
        _releaseFeed = releaseFeed ?? throw new ArgumentNullException(nameof(releaseFeed));
        _updater = updater ?? throw new ArgumentNullException(nameof(updater));
        _installMethodDetector = installMethodDetector ?? throw new ArgumentNullException(nameof(installMethodDetector));
        _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

        Options.Add(PrereleaseOption);
        Options.Add(VersionOption);
        Options.Add(YesOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        // Package-manager installs must go through the owning tool; running
        // the in-place pipeline would leave the package manager's metadata
        // stale and can break its next update. Detect and defer before we
        // even talk to the feed.
        InstallMethod installMethod = _installMethodDetector.Detect();
        if (installMethod.Kind is not InstallMethodKind.Direct)
        {
            _interaction.WriteLine(
                $"This Azure Functions CLI installation is managed by {installMethod.DisplayName}. "
                + $"Run '{installMethod.UpgradeCommand}' to update.");
            return 0;
        }

        bool includePrerelease = parseResult.GetValue(PrereleaseOption);
        string? pinnedVersionRaw = parseResult.GetValue(VersionOption);
        bool yes = parseResult.GetValue(YesOption);

        Release target = await ResolveReleaseAsync(includePrerelease, pinnedVersionRaw, cancellationToken);

        SemVersion? currentVersion = TryParseCurrentVersion(_versionProvider.Version);
        if (currentVersion is not null && SemVersion.PrecedenceComparer.Compare(currentVersion, target.Version) == 0)
        {
            _interaction.WriteSuccess(
                $"Azure Functions CLI is already up to date (version {currentVersion}).");
            return 0;
        }

        string currentDisplay = currentVersion?.ToString() ?? _versionProvider.Version;
        _interaction.WriteLine(
            $"Updating Azure Functions CLI: {currentDisplay} → {target.Version}");

        if (!yes)
        {
            if (!_interaction.IsInteractive)
            {
                throw new GracefulException(
                    "'func update' requires confirmation, but the terminal is non-interactive. "
                    + "Pass '--yes' (or '-y') to accept the update without prompting.",
                    isUserError: true);
            }

            bool confirmed = await _interaction.ConfirmAsync(
                $"Continue and install func {target.Version}?",
                defaultValue: true,
                cancellationToken);
            if (!confirmed)
            {
                _interaction.WriteHint("Update cancelled.");
                return 0;
            }
        }

        try
        {
            await _interaction.RunWithProgressAsync(
                $"Downloading func {target.Version}...",
                async (progressContext, ct) =>
                {
                    var reporter = new ProgressAdapter(progressContext, target.Version.ToString());
                    await _updater.UpdateAsync(target, reporter, ct);
                    return true;
                },
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Feed/updater surfaces a bare InvalidOperationException for
            // recoverable failures (missing artifact, transport error). Wrap
            // as a user-facing error so Program.Main prints it cleanly.
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }

        _interaction.WriteSuccess($"Azure Functions CLI updated to {target.Version}.");
        return 0;
    }

    private async Task<Release> ResolveReleaseAsync(bool includePrerelease, string? pinnedVersionRaw, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(pinnedVersionRaw))
            {
                if (!SemVersion.TryParse(pinnedVersionRaw.Trim(), SemVersionStyles.Strict, out SemVersion? pinnedVersion))
                {
                    throw new GracefulException(
                        $"'--version' expects a semantic version (e.g. 5.1.0 or 5.2.0-preview.1). Got '{pinnedVersionRaw}'.",
                        isUserError: true);
                }

                return await _releaseFeed.GetVersionAsync(pinnedVersion, cancellationToken);
            }

            return await _releaseFeed.GetLatestAsync(includePrerelease, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
    }

    private static SemVersion? TryParseCurrentVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return SemVersion.TryParse(raw.Trim(), SemVersionStyles.Strict, out SemVersion? parsed) ? parsed : null;
    }

    /// <summary>
    /// Bridges the pipeline's byte-oriented <see cref="IProgress{T}"/> onto
    /// the Spectre <see cref="IProgressContext"/> the interaction service
    /// hands us: phase changes update the description, download bytes drive
    /// the percentage.
    /// </summary>
    private sealed class ProgressAdapter(IProgressContext progressContext, string version) : IProgress<UpdateProgress>
    {
        private UpdatePhase? _lastPhase;
        private bool _totalSet;

        public void Report(UpdateProgress value)
        {
            if (_lastPhase != value.Phase)
            {
                _lastPhase = value.Phase;
                progressContext.SetDescription(PhaseDescription(value.Phase, version));

                // Non-download phases don't advertise a total; drop back to
                // indeterminate progress so the bar doesn't stick at 100%.
                if (value.Phase is not UpdatePhase.Downloading)
                {
                    progressContext.SetTotal(null);
                    _totalSet = false;
                }
            }

            if (value.Phase is UpdatePhase.Downloading)
            {
                if (!_totalSet && value.TotalBytes is long total && total > 0)
                {
                    progressContext.SetTotal(total);
                    _totalSet = true;
                }

                if (_totalSet && value.BytesRead is long read)
                {
                    progressContext.Report(read);
                }
            }
        }

        private static string PhaseDescription(UpdatePhase phase, string version) => phase switch
        {
            UpdatePhase.Downloading => $"Downloading func {version}...",
            UpdatePhase.Extracting => "Extracting update package...",
            UpdatePhase.Installing => "Installing update...",
            UpdatePhase.Verifying => "Verifying installation...",
            _ => $"Updating to func {version}...",
        };
    }
}
