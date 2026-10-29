// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Joins retained intake, exact selection, captured-project compatibility, rendering, and create-only preparation.
/// </summary>
internal sealed class PackageTemplateApplication(
    LocalTemplatePackageIntake intake,
    IPackageTemplateAdapter adapter,
    TemplateSelector selector,
    IFunctionsProjectResolver projectResolver,
    ItemTemplateCompatibilityMatcher matcher,
    IFileSystem fileSystem)
{
    private readonly LocalTemplatePackageIntake _intake = intake ?? throw new ArgumentNullException(nameof(intake));
    private readonly IPackageTemplateAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private readonly TemplateSelector _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    private readonly IFunctionsProjectResolver _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
    private readonly ItemTemplateCompatibilityMatcher _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>
    /// Transfers package and native lifetime to an operation whose preview and commit use the same captured output.
    /// </summary>
    /// <exception cref="TemplatePreparationException">Selection, required input, native eligibility, project resolution, or compatibility rejects preparation.</exception>
    /// <exception cref="InvalidTemplatePackageException">The exact selected package fails intake.</exception>
    /// <exception cref="InvalidTemplateMetadataException">Native validation or neutral metadata validation fails.</exception>
    /// <exception cref="NotSupportedException">The adapter cannot translate a native declaration.</exception>
    /// <exception cref="IOException">Intake, native rendering, or capture fails.</exception>
    /// <exception cref="OperationCanceledException">Preparation is canceled.</exception>
    /// <exception cref="AggregateException">Preparation and native cleanup both fail; both failures are retained.</exception>
    public async Task<PackageTemplateOperation> PrepareAsync(PackageTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        TemplatePackageContentLease package = await _intake.IntakeAsync(request.Package, cancellationToken);
        return await PrepareAsync(package, request, cancellationToken);
    }

    /// <summary>
    /// Takes ownership of an already validated package lease for one preparation attempt.
    /// </summary>
    /// <exception cref="TemplatePreparationException">Selection, native eligibility, project resolution, or compatibility rejects preparation.</exception>
    /// <exception cref="InvalidTemplateMetadataException">The package metadata or required project default is invalid.</exception>
    /// <exception cref="IOException">Native rendering or capture fails.</exception>
    /// <exception cref="OperationCanceledException">Preparation is canceled.</exception>
    /// <exception cref="AggregateException">Preparation and cleanup independently fail or are canceled.</exception>
    public async Task<PackageTemplateOperation> PrepareAsync(
        TemplatePackageContentLease package, TemplatePreparationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(request);
        bool transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkingDirectory target = new(new DirectoryInfo(request.TargetDirectory), WasExplicit: true, OriginalPath: request.TargetDirectory);
            FunctionsProject? project = request.Selection.Type == TemplateType.Item
                ? await ResolveProjectAsync(target, cancellationToken)
                : null;
            TemplateEngineContext context = new(target, project is null
                ? null
                : new(project.WorkingDirectory, project.StackName, project.Language));
            PackageTemplateSession session = await _adapter.OpenAsync(package, context, cancellationToken);
            RetainedResources resources = new(package, session);
            transferred = true;
            Task<PackageTemplateOperation> preparation = PrepareBoundAsync(request, package, session, resources, project, cancellationToken);
            try
            {
                return await preparation;
            }
            finally
            {
                if (!preparation.IsCompletedSuccessfully)
                {
                    Task cleanup = resources.DisposeAsync().AsTask();
                    try
                    {
                        await cleanup;
                    }
                    finally
                    {
                        if (!cleanup.IsCompletedSuccessfully)
                        {
                            List<Exception> failures = preparation.IsCanceled
                                ? [new TaskCanceledException(preparation)]
                                : [.. preparation.Exception!.InnerExceptions];
                            failures.AddRange(cleanup.IsCanceled ? [new TaskCanceledException(cleanup)] : cleanup.Exception!.InnerExceptions);
                            throw new AggregateException("Template preparation and resource cleanup both failed.", failures);
                        }
                    }
                }
            }
        }
        finally
        {
            if (!transferred)
            {
                package.Dispose();
            }
        }
    }

    internal async Task<FunctionsProject> ResolveProjectAsync(WorkingDirectory target, CancellationToken cancellationToken)
    {
        ProjectResolutionResult result = await _projectResolver.ResolveProjectAsync(new(target) { InspectCapabilities = true }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is not ProjectResolutionResult.Resolved resolved)
        {
            var notResolved = (ProjectResolutionResult.NotResolved)result;
            throw new TemplatePreparationException(
                TemplatePreparationFailure.ProjectNotResolved, notResolved.Message, projectFailure: notResolved.Failure);
        }

        string projectRoot = PreparedOperationPlan.NormalizeTargetDirectory(resolved.Project.WorkingDirectory.Info.FullName);
        if (!string.Equals(projectRoot, target.Info.FullName, StringComparison.Ordinal))
        {
            throw new TemplatePreparationException(TemplatePreparationFailure.ProjectMismatch,
                "The resolved project root differs from the requested template target. Run the command from the actual project root.");
        }

        return resolved.Project;
    }

    private async Task<PackageTemplateOperation> PrepareBoundAsync(
        TemplatePreparationRequest request,
        TemplatePackageContentLease package,
        PackageTemplateSession session,
        RetainedResources resources,
        FunctionsProject? project,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RequiredProjectDefault is string projectDefault)
        {
            ValidateProjectDefault(session, projectDefault);
        }

        TemplateSelectionResult selection = _selector.Select([.. session.Candidates.Select(candidate => candidate.Descriptor)], request.Selection);
        if (selection.Selected is not { } arguments)
        {
            throw new TemplatePreparationException(TemplatePreparationFailure.SelectionRejected,
                string.Join("\n", selection.Diagnostics), selection);
        }

        if (!arguments.IsComplete)
        {
            throw new TemplatePreparationException(TemplatePreparationFailure.MissingRequiredInput,
                $"Template '{arguments.Template.Metadata.Identity}' requires: {string.Join(", ", arguments.MissingRequiredParameters.Select(parameter => parameter.LongName))}. Supply the required template arguments.",
                selection);
        }

        PackageTemplateCandidate candidate = session.Candidates.Single(candidate => ReferenceEquals(candidate.Descriptor, arguments.Template));
        candidate.EnsureEligible();

        ItemTemplateCompatibilityResult? compatibility = null;
        if (project is not null)
        {
            ItemTemplateRequirements requirements = candidate.Descriptor.Requirements;
            compatibility = _matcher.Evaluate(project, requirements, cancellationToken);
            if (!compatibility.PassesProjectCapabilityCheck)
            {
                throw new TemplatePreparationException(TemplatePreparationFailure.ProjectIncompatible,
                    "The item cannot be prepared because the project capability check failed. Review the compatibility diagnostics.",
                    selection, compatibility);
            }
        }

        PackageTemplateCapture capture = await candidate.RenderAsync(arguments, request.Name, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if ((request.Selection.Type == TemplateType.Project) != (capture.Configuration is not null))
        {
            throw new InvalidOperationException("Project capture requires verified CLI configuration; item capture must not supply it.");
        }

        PreparedOperationPlan plan = new(
            request.TargetDirectory,
            new(package.PackageId, package.Version, candidate.Descriptor.Metadata.Identity),
            capture.Entries,
            capture.Configuration,
            compatibility);
        PreparedOperation operation = new(plan, _fileSystem, resources);
        return new(operation, package);
    }

    internal static void ValidateProjectDefault(PackageTemplateSession session, string identity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        PackageTemplateCandidate[] defaults = [.. session.Candidates.Where(candidate =>
            string.Equals(candidate.Descriptor.Metadata.Identity, identity, StringComparison.Ordinal))];
        if (defaults.Length != 1 || defaults[0].Descriptor.Metadata.Type != TemplateType.Project
            || defaults[0].Descriptor.Metadata.DeclaredInternalRole is not null)
        {
            throw new InvalidTemplateMetadataException($"The bound package must contain exactly one ordinary project default with identity '{identity}'.");
        }
    }

    private sealed class RetainedResources(TemplatePackageContentLease package, PackageTemplateSession session) : IAsyncDisposable
    {
        private readonly TemplatePackageContentLease _package = package ?? throw new ArgumentNullException(nameof(package));
        private readonly PackageTemplateSession _session = session ?? throw new ArgumentNullException(nameof(session));

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _session.DisposeAsync();
            }
            finally
            {
                _package.Dispose();
            }
        }
    }
}
