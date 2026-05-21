// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
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
    private readonly ILogger<ValidateExtensionBundleInitializationStep> _logger;

    public ValidateExtensionBundleInitializationStep(
        IExtensionBundleResolver resolver,
        ILogger<ValidateExtensionBundleInitializationStep> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
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

        DirectoryInfo projectDir = context.Options.WorkingDirectory.Info;
        string hostJsonPath = Path.Combine(projectDir.FullName, "host.json");
        HostJsonBundleSection? section = TryReadBundleSection(hostJsonPath);

        if (section is null)
        {
            _logger.LogInformation("No extension bundle declared in host.json; skipping bundle resolution.");
            return StartInitializationStepResult.Completed("No extension bundle declared");
        }

        var projectContext = new ExtensionBundleProjectContext(
            BundleId: section.Id,
            HostJsonVersionRange: section.Version,
            WorkerRuntime: ResolveWorkerRuntime(context),
            ProfileName: NullIfNone(context.Initialization.ProfileName),
            ProfileBundleVersionRange: null);

        ExtensionBundleResolution resolution = await _resolver.ResolveAsync(projectContext, cancellationToken).ConfigureAwait(false);

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
                throw new InvalidOperationException(missing.Hint);

            case ExtensionBundleResolution.EmptyIntersection empty:
                _logger.LogError("{Hint}", empty.Hint);
                throw new InvalidOperationException(empty.Hint);

            case ExtensionBundleResolution.NoCompatibleInstall none:
                _logger.LogError("{Hint}", none.Hint);
                throw new InvalidOperationException(none.Hint);

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

    private static HostJsonBundleSection? TryReadBundleSection(string hostJsonPath)
    {
        if (!File.Exists(hostJsonPath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(hostJsonPath);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (!doc.RootElement.TryGetProperty("extensionBundle", out JsonElement bundle))
            {
                return null;
            }

            string id = bundle.TryGetProperty("id", out JsonElement idElem) ? idElem.GetString() ?? string.Empty : string.Empty;
            string version = bundle.TryGetProperty("version", out JsonElement verElem) ? verElem.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            return new HostJsonBundleSection(id, version);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private sealed record HostJsonBundleSection(string Id, string Version);
}
