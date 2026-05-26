// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Resolves the project's extension bundle via <see cref="IExtensionBundleResolver"/> and stages the host env vars.
/// </summary>
internal sealed class ValidateExtensionBundleInitializationStep : DemoInitializationStep
{
    public const string StepId = "resolve_bundle";

    private const string DownloadPathEnvVar = "AzureFunctionsJobHost__extensionBundle__downloadPath";
    private const string EnsureLatestEnvVar = "AzureFunctionsJobHost__extensionBundle__ensureLatest";

    private readonly IExtensionBundleResolver _resolver;
    private readonly IHostJsonBundleSectionReader _bundleSectionReader;
    private readonly ILogger<ValidateExtensionBundleInitializationStep> _logger;

    public ValidateExtensionBundleInitializationStep(
        IExtensionBundleResolver resolver,
        IHostJsonBundleSectionReader bundleSectionReader,
        ILogger<ValidateExtensionBundleInitializationStep> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bundleSectionReader = bundleSectionReader ?? throw new ArgumentNullException(nameof(bundleSectionReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Id => StepId;

    public override string Title => "Resolve extension bundle";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.State.Project is not { SupportsExtensionBundles: true } project)
        {
            string stackName = context.State.Project?.StackDisplayName ?? "unknown";
            return StartInitializationStepResult.Completed($"No extension bundle required for {stackName}");
        }

        HostJsonBundleSection? section;
        try
        {
            section = await _bundleSectionReader.ReadAsync(project.WorkingDirectory.Info, cancellationToken);
        }
        catch (ExtensionBundleConfigurationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        if (section is null)
        {
            _logger.LogInformation("No extension bundle declared in host.json; skipping bundle resolution.");
            return StartInitializationStepResult.Completed("No extension bundle declared");
        }

        var projectContext = new ExtensionBundleProjectContext(
            BundleId: section.Id,
            HostJsonVersionRange: section.Version,
            WorkerRuntime: ResolveWorkerRuntime(context),
            ProfileName: NullIfNone(context.State.ProfileName),
            ProfileBundleVersionRange: RangeText(context.State.ResolvedProfile?.ExtensionBundleVersionRange));

        ExtensionBundleResolution resolution = await _resolver.ResolveAsync(projectContext, cancellationToken);

        switch (resolution)
        {
            case ExtensionBundleResolution.Resolved r:
                context.State.BundleDownloadPath = r.Path;
                context.State.BundleVersion = r.Version;
                context.State.BundleEnvVarsForHost[DownloadPathEnvVar] = r.Path;
                context.State.BundleEnvVarsForHost[EnsureLatestEnvVar] = "false";
                if (r.RuntimeWarning is { } warn)
                {
                    _logger.LogWarning(
                        "Profile '{Profile}' does not list worker runtime '{Runtime}' as supported (declared: {Supported}).",
                        warn.ProfileName, warn.WorkerRuntime, string.Join(", ", warn.SupportedRuntimes));
                }

                return StartInitializationStepResult.Completed($"Bundle {r.BundleId} {r.Version}");

            case ExtensionBundleResolution.WorkloadMissing missing:
                _logger.LogError("{Hint}", missing.Hint);
                throw new GracefulException(missing.Hint, isUserError: true);

            case ExtensionBundleResolution.EmptyIntersection empty:
                _logger.LogError("{Hint}", empty.Hint);
                throw new GracefulException(empty.Hint, isUserError: true);

            case ExtensionBundleResolution.NoCompatibleInstall none:
                _logger.LogError("{Hint}", none.Hint);
                throw new GracefulException(none.Hint, isUserError: true);

            default:
                throw new InvalidOperationException($"Unknown resolution variant: {resolution.GetType().Name}");
        }
    }

    private static string? ResolveWorkerRuntime(StartInitializationStepContext context)
        => context.State.Project?.StackName?.ToLowerInvariant();

    private static string? NullIfNone(string? profile) =>
        string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : profile;

    private static string? RangeText(NuGet.Versioning.VersionRange? range) => range?.OriginalString ?? range?.ToString();
}
