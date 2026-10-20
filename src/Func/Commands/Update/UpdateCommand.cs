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
/// Resolves and installs Azure Functions CLI updates for installations that
/// explicitly declare support for in-place updates.
/// </summary>
internal sealed class UpdateCommand : FuncCliCommand, IBuiltInCommand
{
    private const string InstallationOptionsUrl = "https://aka.ms/func-cli";

    private readonly IReleaseFeed _releaseFeed;
    private readonly ICliUpdater _updater;
    private readonly IInstallationMetadataReader _installationMetadataReader;
    private readonly ICliVersionProvider _versionProvider;
    private readonly IInteractionService _interaction;

    public UpdateCommand(
        IReleaseFeed releaseFeed,
        ICliUpdater updater,
        IInstallationMetadataReader installationMetadataReader,
        ICliVersionProvider versionProvider,
        IInteractionService interaction)
        : base("update", "Update Azure Functions CLI. Defaults to the latest stable release; "
            + "use '--prerelease' for previews or '--version' for a specific version.")
    {
        _releaseFeed = releaseFeed ?? throw new ArgumentNullException(nameof(releaseFeed));
        _updater = updater ?? throw new ArgumentNullException(nameof(updater));
        _installationMetadataReader = installationMetadataReader ?? throw new ArgumentNullException(nameof(installationMetadataReader));
        _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

        Options.Add(PrereleaseOption);
        Options.Add(VersionOption);
        Options.Add(YesOption);
    }

    public Option<bool> PrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Use the latest release, including prerelease versions.",
    };

    public Option<string?> VersionOption { get; } = new("--version")
    {
        Description = "Install a specific semantic version, including an older version when explicitly requested.",
    };

    public Option<bool> YesOption { get; } = new("--yes", "-y")
    {
        Description = "Skip the update confirmation prompt.",
    };

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        InstallationMetadata installationMetadata = await ReadInstallationMetadataAsync(cancellationToken);
        if (installationMetadata.UpdatePolicy is InstallationUpdatePolicy.External)
        {
            RenderExternalUpdateGuidance();
            return 0;
        }

        if (installationMetadata.UpdatePolicy is not InstallationUpdatePolicy.InPlace)
        {
            throw CreateInstallationMetadataException(
                new InvalidInstallationMetadataException(
                    "The installation metadata contains an unsupported update policy."));
        }

        bool includePrerelease = parseResult.GetValue(PrereleaseOption);
        string? pinnedVersionRaw = parseResult.GetValue(VersionOption);
        bool isPinnedVersion = pinnedVersionRaw is not null;
        bool yes = parseResult.GetValue(YesOption);

        Release target = await ResolveReleaseAsync(includePrerelease, pinnedVersionRaw, cancellationToken);
        SemVersion? currentVersion = TryParseCurrentVersion(_versionProvider.Version);
        int? versionComparison = currentVersion is null
            ? null
            : SemVersion.PrecedenceComparer.Compare(currentVersion, target.Version);

        if (versionComparison == 0)
        {
            _interaction.WriteSuccess($"Azure Functions CLI is already up to date (version {currentVersion}).");
            return 0;
        }

        if (versionComparison > 0 && !isPinnedVersion)
        {
            _interaction.WriteSuccess(
                $"Azure Functions CLI {currentVersion} is newer than the latest available version {target.Version}. No update was performed.");
            return 0;
        }

        string currentDisplay = currentVersion?.ToString() ?? _versionProvider.Version;
        string operation = versionComparison > 0 ? "Downgrading" : "Updating";
        _interaction.WriteLine($"{operation} Azure Functions CLI: {currentDisplay} -> {target.Version}");

        if (!yes)
        {
            bool confirmed = await _interaction.ConfirmAsync(
                $"Continue and install Azure Functions CLI {target.Version}?",
                defaultValue: true,
                whenInputUnavailable: false,
                cancellationToken);
            if (!confirmed)
            {
                _interaction.WriteHint("Update cancelled.");
                return 0;
            }
        }

        await _interaction.RunWithProgressAsync(
            $"Downloading Azure Functions CLI {target.Version}...",
            async (progressContext, ct) =>
            {
                var reporter = new ProgressAdapter(progressContext, target.Version.ToString());
                await _updater.UpdateAsync(target, reporter, ct);
                return true;
            },
            cancellationToken);

        _interaction.WriteSuccess($"Azure Functions CLI updated to {target.Version}.");
        return 0;
    }

    private async Task<InstallationMetadata> ReadInstallationMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _installationMetadataReader.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or InvalidInstallationMetadataException
                or InvalidOperationException or UnauthorizedAccessException)
        {
            throw CreateInstallationMetadataException(ex);
        }
    }

    private void RenderExternalUpdateGuidance()
    {
        _interaction.WriteLine(
            "This Azure Functions CLI installation is managed externally. "
            + "Use the same installer or package manager that installed it to update. "
            + $"For installation options, visit {InstallationOptionsUrl}.");
    }

    private async Task<Release> ResolveReleaseAsync(bool includePrerelease, string? pinnedVersionRaw, CancellationToken cancellationToken)
    {
        if (pinnedVersionRaw is not null)
        {
            if (!SemVersion.TryParse(pinnedVersionRaw.Trim(), SemVersionStyles.Strict, out SemVersion? pinnedVersion))
            {
                throw new GracefulException(
                    $"'--version' expects a semantic version such as '5.1.0' or '5.2.0-preview.1'. Got '{pinnedVersionRaw}'.",
                    isUserError: true);
            }

            try
            {
                return await _releaseFeed.GetVersionAsync(pinnedVersion, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                throw new GracefulException(ex.Message, ex, isUserError: true);
            }
        }

        try
        {
            return await _releaseFeed.GetLatestAsync(includePrerelease, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
    }

    private static GracefulException CreateInstallationMetadataException(Exception innerException) =>
        new(
            "Azure Functions CLI could not verify how this installation is managed. "
            + $"Reinstall Azure Functions CLI using an option from {InstallationOptionsUrl}, then run 'func update' again.",
            innerException,
            isUserError: true);

    private static SemVersion? TryParseCurrentVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return SemVersion.TryParse(raw.Trim(), SemVersionStyles.Strict, out SemVersion? parsed) ? parsed : null;
    }

    private sealed class ProgressAdapter(IProgressContext progressContext, string version) : IProgress<UpdateProgress>
    {
        private UpdatePhase? _lastPhase;
        private bool _totalSet;

        public void Report(UpdateProgress value)
        {
            if (_lastPhase != value.Phase)
            {
                _lastPhase = value.Phase;
                progressContext.SetDescription(GetPhaseDescription(value.Phase, version));

                if (value.Phase is not UpdatePhase.Downloading)
                {
                    progressContext.SetTotal(null);
                    _totalSet = false;
                }
            }

            if (value.Phase is not UpdatePhase.Downloading)
            {
                return;
            }

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

        private static string GetPhaseDescription(UpdatePhase phase, string version) => phase switch
        {
            UpdatePhase.Downloading => $"Downloading Azure Functions CLI {version}...",
            UpdatePhase.Extracting => "Extracting update package...",
            UpdatePhase.Installing => "Installing update...",
            UpdatePhase.Verifying => "Verifying installation...",
            _ => $"Updating Azure Functions CLI to {version}...",
        };
    }
}
