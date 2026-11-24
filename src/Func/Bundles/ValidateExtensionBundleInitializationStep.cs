// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Resolves the project's extension bundle via <see cref="IExtensionBundleResolver"/> and stages the host env vars.
/// </summary>
internal sealed class ValidateExtensionBundleInitializationStep : FuncStartInitializationStep
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
            string stackName = context.State.Project?.Stack.Descriptor.DisplayName ?? "unknown";
            return StartInitializationStepResult.Completed($"No extension bundle required for {stackName}");
        }

        HostJsonBundleSection? section;
        try
        {
            section = await _bundleSectionReader.ReadAsync(project.WorkingDirectory.Info, cancellationToken);
        }
        catch (ExtensionBundleConfigurationException ex)
        {
            throw new GracefulException(
                $"{ex.Message} Run 'func run' again after correcting the issue.",
                isUserError: true,
                verboseMessage: ex.ToString());
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

        return resolution switch
        {
            ExtensionBundleResolution.Resolved resolved => UseResolvedBundle(context, resolved),
            ExtensionBundleResolution.NotResolved notResolved => Fail(notResolved.Message, notResolved.RuntimeWarning),
            ExtensionBundleResolution.WorkloadMissing missing => Fail(missing.Hint),
            ExtensionBundleResolution.EmptyIntersection empty => Fail(empty.Hint),
            ExtensionBundleResolution.NoCompatibleInstall none => Fail(none.Hint),
        };
    }

    private StartInitializationStepResult UseResolvedBundle(StartInitializationStepContext context, ExtensionBundleResolution.Resolved resolved)
    {
        context.State.BundleDownloadPath = resolved.Path;
        context.State.BundleVersion = resolved.Version;
        context.State.BundleEnvVarsForHost[DownloadPathEnvVar] = resolved.Path;
        context.State.BundleEnvVarsForHost[EnsureLatestEnvVar] = "false";
        LogRuntimeWarning(resolved.RuntimeWarning);

        return StartInitializationStepResult.Completed($"Bundle {resolved.BundleId} {resolved.Version}");
    }

    private StartInitializationStepResult Fail(string message, ExtensionBundleSupportedRuntimeWarning? runtimeWarning = null)
    {
        LogRuntimeWarning(runtimeWarning);
        _logger.LogError("{Hint}", message);
        throw new GracefulException(message, isUserError: true);
    }

    private void LogRuntimeWarning(ExtensionBundleSupportedRuntimeWarning? runtimeWarning)
    {
        if (runtimeWarning is { } warn)
        {
            _logger.LogWarning(
                "Profile '{Profile}' does not list worker runtime '{Runtime}' as supported (declared: {Supported}).",
                warn.ProfileName, warn.WorkerRuntime, string.Join(", ", warn.SupportedRuntimes));
        }
    }

    private static string? ResolveWorkerRuntime(StartInitializationStepContext context)
        => context.State.Worker?.WorkerRuntime;

    private static string? NullIfNone(string? profile) =>
        string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : profile;

    private static string? RangeText(NuGet.Versioning.VersionRange? range) => range?.OriginalString ?? range?.ToString();
}
