// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Mutable state produced while startup initialization steps run.
/// </summary>
internal sealed class StartInitializationState
{
    public string ProfileName { get; set; } = "none";

    public ProfileResolution? ProfileResolution { get; set; }

    public ResolvedProfile? ResolvedProfile { get; set; }

    public string? HostVersion { get; set; }

    public ContentWorkloadInfo? HostWorkload { get; set; }

    public FunctionsProject? Project { get; set; }

    public IFunctionsWorker? Worker { get; set; }

    public FunctionsProjectHostRunContext? HostRunContext { get; set; }

    public string? BundleVersion { get; set; }

    public string? BundleDownloadPath { get; set; }

    // TODO: This should move to host environment variables
    public Dictionary<string, string> BundleEnvVarsForHost { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> HostEnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IHostEventStream? EventStream { get; set; }

    /// <summary>
    /// Disposable handle for the managed Azurite process the CLI launched
    /// for this run, or null when there is no managed process (user-managed,
    /// non-local storage, or <c>--no-azurite</c>). The <see cref="StartCommand"/>
    /// owns disposal once <see cref="ToResult"/> has been called.
    /// </summary>
    public ManagedAzuriteHandle? ManagedAzurite { get; set; }

    /// <summary>
    /// On-disk paths for the managed Azurite instance the CLI launched, or
    /// null when Azurite is user-managed, disabled, or non-local. Lets the
    /// host run surface reset guidance if the emulator returns a 500.
    /// </summary>
    public AzuriteManagedPaths? AzuritePaths { get; set; }

    public StartInitializationResult ToResult(StartInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string hostVersion = HostVersion ?? throw new InvalidOperationException("Host version was not resolved.");
        FunctionsProject project = Project ?? throw new InvalidOperationException("Functions project was not resolved.");
        IFunctionsWorker worker = Worker ?? throw new InvalidOperationException("Functions worker was not resolved.");
        FunctionsProjectHostRunContext hostRunContext = HostRunContext
            ?? throw new InvalidOperationException("Host run context was not prepared.");
        IHostEventStream eventStream = EventStream ?? throw new InvalidOperationException("Host event stream was not initialized.");

        var runInfo = new DashboardRunInfo(context.CliVersion, ProfileName, project.StackDisplayName);

        return new StartInitializationResult(
            runInfo,
            eventStream,
            hostVersion,
            project.SupportsExtensionBundles,
            BundleVersion,
            project,
            worker,
            hostRunContext,
            CreateProfileInfo(),
            ManagedAzurite,
            AzuritePaths);
    }

    private StartInitializationProfileInfo? CreateProfileInfo()
    {
        if (ResolvedProfile is not { } profile)
        {
            return null;
        }

        List<StartInitializationProfileDiagnostic> diagnostics = [];
        if (ProfileResolution is not null)
        {
            foreach (ProfileDiagnostic diagnostic in ProfileResolution.Diagnostics)
            {
                var profileDiagnostic = new StartInitializationProfileDiagnostic(
                    diagnostic.Severity.ToString().ToLowerInvariant(),
                    diagnostic.Message);
                diagnostics.Add(profileDiagnostic);
            }
        }

        IReadOnlyDictionary<string, string> workerVersionRanges = profile.WorkerVersionRanges.ToDictionary(
            static pair => pair.Key,
            static pair => RangeText(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        string? extensionBundleVersionRange = profile.ExtensionBundleVersionRange is null
            ? null
            : RangeText(profile.ExtensionBundleVersionRange);

        return new StartInitializationProfileInfo(
            profile.Name,
            profile.Source.Kind.ToString().ToLowerInvariant(),
            profile.Source.DisplayName,
            RangeText(profile.HostVersionRange),
            workerVersionRanges,
            extensionBundleVersionRange,
            profile.SupportedRuntimes,
            diagnostics);
    }

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();
}
