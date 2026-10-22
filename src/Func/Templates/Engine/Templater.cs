// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
internal sealed class Templater(TemplateEngineSession session, TemplateInvocationCoordinator invocation) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly TemplateInvocationCoordinator _invocation = ValidateInvocation(session, invocation);
    private IReadOnlyList<Candidate>? _candidates;
    private IReadOnlyList<MalformedTypeDescriptor> _malformedTypes = [];

    public TemplateEngineContext Context => _session.Context;

    internal async Task<TemplateResolutionResult> BindProjectAsync(TemplateCatalogEntry entry, NativeTemplateSelection selection,
        NativeTemplateArtifactVerifier verifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(verifier);
        return await BindProjectAsync(entry, selection.TemplateIdentity,
            (template, token) => verifier.VerifyAsync(_session, template, selection, token), cancellationToken);
    }

    internal async Task<TemplateResolutionResult> BindProjectAsync(TemplateCatalogEntry entry, BundledBasicSelection selection,
        BundledBasicArtifactVerifier verifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(verifier);
        return await BindProjectAsync(entry, selection.TemplateIdentity,
            (template, token) => verifier.VerifyAsync(_session, template, selection, token), cancellationToken);
    }

    private async Task<TemplateResolutionResult> BindProjectAsync(TemplateCatalogEntry entry, string identity,
        Func<ITemplateInfo, CancellationToken, Task> verify, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using IDisposable operation = _session.EnterBoundOperation();
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        Candidate candidate = candidates.SingleOrDefault(candidate => ReferenceEquals(candidate.Entry, entry))
            ?? throw new ArgumentException("The selected entry must originate from this fresh template flow.", nameof(entry));
        if (entry.Type != TemplateType.Project || entry.Identity != identity)
        {
            throw new InvalidTemplateMetadataException("The bound selection is not the exact project template identity.");
        }
        await ValidateAsync(cancellationToken);
        if (!IsEligible(candidate))
        {
            return CreateGroup([candidate]);
        }
        _candidates = Array.AsReadOnly<Candidate>([.. candidates.Select(item => ReferenceEquals(item, candidate)
            ? candidate with { Configuration = candidate.Configuration! with { ValidateArtifactAsync = ValidateAsync } }
            : item)]);
        return await CreateGroupAsync([entry], cancellationToken);

        async Task ValidateAsync(CancellationToken token)
        {
            try
            {
                await verify(candidate.Template, token);
            }
            catch (Exception exception) when (exception is BasicAcquisitionException or Workloads.Discovery.InvalidWorkloadException)
            {
                throw new InvalidTemplateMetadataException($"Exact artifact binding failed: {exception.Message}", exception);
            }
        }
    }

    internal async Task<IReadOnlyList<InstalledTemplateArtifact>> ProjectArtifactsAsync(NativeTemplateArtifactVerifier verifier,
        NativeTemplateOwnerIdentity owner, BundleChannel? channel, CancellationToken cancellationToken,
        BundledBasicArtifactVerifier? bundledVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(owner);
        using IDisposable operation = _session.EnterBoundOperation();
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        List<InstalledTemplateArtifact> result = [];
        foreach (Candidate candidate in candidates.Where(candidate => candidate.Entry.Type == TemplateType.Project))
        {
            try
            {
                BasicAcquisitionException externalFailure;
                try
                {
                    NativeTemplateSelection selection = await verifier.ProjectAsync(_session, candidate.Template, owner, channel, cancellationToken);
                    result.Add(new(candidate.Entry, selection, Diagnostic(candidate)));
                    continue;
                }
                catch (BasicAcquisitionException exception) when (exception.Failure is not
                    (BasicAcquisitionFailure.PlanChanged or BasicAcquisitionFailure.RecoveryRequired))
                {
                    externalFailure = exception;
                }
                BundledBasicSelection? bundled = bundledVerifier is null ? null
                    : await bundledVerifier.ProjectAsync(_session, candidate.Template, owner, cancellationToken);
                if (bundled is not null)
                {
                    result.Add(new(candidate.Entry, null, Diagnostic(candidate)) { BundledSelection = bundled });
                    continue;
                }
                result.Add(new(candidate.Entry, null, externalFailure.Message) { Failure = externalFailure });
            }
            catch (BasicAcquisitionException exception) when (exception.Failure is not
                (BasicAcquisitionFailure.PlanChanged or BasicAcquisitionFailure.RecoveryRequired))
            {
                result.Add(new(candidate.Entry, null, exception.Message) { Failure = exception });
            }
            catch (Exception exception) when (exception is Workloads.Discovery.InvalidWorkloadException
                or InvalidTemplateMetadataException or IOException or UnauthorizedAccessException)
            {
                result.Add(new(candidate.Entry, null, exception.Message) { Failure = exception });
            }
        }
        return result.AsReadOnly();

        static string? Diagnostic(Candidate candidate) => IsEligible(candidate) ? null
            : candidate.Entry.MetadataDiagnostic ?? candidate.Entry.ProjectConfigurationDiagnostic
                ?? string.Join(" ", candidate.Entry.ConstraintEvaluation.Diagnostics.Select(item => item.Message));
    }

    /// <summary>
    /// Lists the installed templates of <paramref name="type"/>, including hidden templates and templates whose func
    /// host metadata can't be used.
    /// </summary>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        return Array.AsReadOnly<TemplateCatalogEntry>([.. candidates.Where(candidate => candidate.Entry.Type == type).Select(candidate => candidate.Entry)]);
    }

    public async Task<TemplateResolutionResult> ResolveGroupAsync(string reference, TemplateType type, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        Candidate[] matches = [.. candidates.Where(candidate => string.Equals(candidate.Entry.Identity, reference, StringComparison.Ordinal))];
        MalformedTypeDescriptor[] malformed = [.. _malformedTypes.Where(template => string.Equals(template.Identity, reference, StringComparison.Ordinal))];
        if (matches.Length == 0 && malformed.Length == 0)
        {
            matches = [.. candidates.Where(candidate => candidate.Entry.ShortNames.Any(name => string.Equals(name, reference, StringComparison.OrdinalIgnoreCase)))];
            malformed = [.. _malformedTypes.Where(template => template.ShortNames.Any(name => string.Equals(name, reference, StringComparison.OrdinalIgnoreCase)))];
        }

        if (malformed.Length > 0 && matches.Length == 0)
        {
            return new(TemplateResolutionStatus.InvalidTemplateType, null, [], $"Installed template '{malformed[0].Identity}' must declare an item or project type.");
        }

        if (matches.Length == 0)
        {
            return new(TemplateResolutionStatus.NotFound, null, []);
        }

        Candidate[] typed = [.. matches.Where(candidate => candidate.Entry.Type == type)];
        IReadOnlyList<TemplateCatalogEntry> wrongType = Array.AsReadOnly<TemplateCatalogEntry>([.. matches.Where(candidate => candidate.Entry.Type != type).Select(candidate => candidate.Entry)]);
        if (typed.Length == 0)
        {
            return new(TemplateResolutionStatus.WrongType, null, Array.AsReadOnly<TemplateCatalogEntry>([.. matches.Select(candidate => candidate.Entry)]));
        }

        Candidate[] eligible = [.. typed.Where(IsEligible)];
        if (eligible.Length == 0)
        {
            return CreateGroup(typed, wrongType);
        }

        IGrouping<string, Candidate>[] groups = [.. eligible.GroupBy(candidate => candidate.Entry.GroupIdentity ?? candidate.Entry.Identity, StringComparer.Ordinal)];
        if (groups.Length != 1)
        {
            return new(TemplateResolutionStatus.AmbiguousGroup, null, Array.AsReadOnly<TemplateCatalogEntry>([.. typed.Select(candidate => candidate.Entry), .. wrongType]));
        }

        string selectedGroup = groups[0].Key;
        Candidate[] selected = [.. typed.Where(candidate => string.Equals(candidate.Entry.GroupIdentity ?? candidate.Entry.Identity, selectedGroup, StringComparison.Ordinal))];
        IReadOnlyList<TemplateCatalogEntry> rejectedGroups = Array.AsReadOnly<TemplateCatalogEntry>([.. wrongType,
            .. typed.Where(candidate => !string.Equals(candidate.Entry.GroupIdentity ?? candidate.Entry.Identity, selectedGroup, StringComparison.Ordinal)).Select(candidate => candidate.Entry)]);
        return CreateGroup(selected, rejectedGroups);
    }

    public async Task<TemplateResolutionResult> CreateGroupAsync(IReadOnlyList<TemplateCatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        List<Candidate> selected = [];
        foreach (TemplateCatalogEntry entry in entries)
        {
            Candidate? candidate = candidates.FirstOrDefault(candidate => ReferenceEquals(candidate.Entry, entry));
            if (candidate is null)
            {
                throw new ArgumentException("Group entries must originate from this template flow.", nameof(entries));
            }

            selected.Add(candidate);
        }

        if (selected.Select(candidate => candidate.Entry.Type).Distinct().Count() > 1
            || selected.Select(candidate => candidate.Entry.GroupIdentity ?? candidate.Entry.Identity).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return new(TemplateResolutionStatus.AmbiguousGroup, null, Array.AsReadOnly<TemplateCatalogEntry>([.. selected.Select(candidate => candidate.Entry)]));
        }

        return CreateGroup(selected);
    }

    private TemplateResolutionResult CreateGroup(IReadOnlyList<Candidate> candidates, IReadOnlyList<TemplateCatalogEntry>? additionalDiagnostics = null)
    {
        List<ResolvedTemplate> items = [];
        List<TemplateCatalogEntry> diagnostics = [.. additionalDiagnostics ?? []];
        foreach (Candidate candidate in candidates.OrderBy(candidate => candidate.Entry.Identity, StringComparer.Ordinal))
        {
            if (!IsEligible(candidate))
            {
                diagnostics.Add(candidate.Entry);
                continue;
            }

            items.Add(new(candidate.Entry, new SelectedTemplateInvoker(candidate.Template, candidate.Configuration!, _invocation,
                candidate.Configuration!.ValidateArtifactAsync is null ? null : _session)));
        }

        TemplateGroup group = new(items.AsReadOnly(), diagnostics.AsReadOnly());
        return new(items.Count > 0 ? TemplateResolutionStatus.Success : TemplateResolutionStatus.Unavailable, group, group.Diagnostics);
    }

    private async Task<IReadOnlyList<Candidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        TemplatePackageManager packageManager = _session.PackageManager;
        cancellationToken.ThrowIfCancellationRequested();
        if (_candidates is not null)
        {
            return _candidates;
        }

        IReadOnlyList<ITemplateInfo> templates = await packageManager.GetTemplatesAsync(cancellationToken);
        IReadOnlyList<ITemplatePackage> packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        _session.CheckStoreDiagnostics();
        _malformedTypes = Array.AsReadOnly<MalformedTypeDescriptor>([.. templates.Where(template => TemplateCatalogProjector.GetTemplateType(template) is null)
            .Select(template => new MalformedTypeDescriptor(template.Identity, Array.AsReadOnly<string>([.. template.ShortNameList])))]);
        List<Candidate> candidates = [];
        Dictionary<string, Task<IReadOnlyCollection<string>>> packageMetadata = new(StringComparer.Ordinal);
        foreach (ITemplateInfo template in templates.Where(template => TemplateCatalogProjector.GetTemplateType(template) is not null))
        {
            ITemplatePackage? package = packages.FirstOrDefault(package => package.MountPointUri == template.MountPointUri);
            TemplateCatalogEntry entry = Freeze(TemplateCatalogProjector.Project(template, package));

            try
            {
                TemplateConfigurationSnapshot configuration = await _session.ReadConfigurationAsync(template, cancellationToken);
                if (configuration.Type is null || configuration.Type != entry.Type)
                {
                    candidates.Add(FailedCandidate(template, entry, "Current mounted template type is missing, unrecognized, or differs from its cached descriptor. Correct the type and refresh the installed template before rerunning selection."));
                    continue;
                }

                entry = Freeze(TemplateCatalogProjector.Project(template, package, configuration));
                if (!packageMetadata.TryGetValue(template.MountPointUri, out Task<IReadOnlyCollection<string>>? metadata))
                {
                    metadata = _session.ReadPackageTypesAsync(package, cancellationToken);
                    packageMetadata.Add(template.MountPointUri, metadata);
                }

                IReadOnlyCollection<string> packageTypes = await metadata;
                TemplateConstraintEvaluation evaluation = await _session.EvaluateConstraintsAsync(configuration.Root, cancellationToken, packageTypes);
                if (entry.Type == TemplateType.Project && _invocation.ProjectTemplatesEnabled)
                {
                    try
                    {
                        entry = entry with { ProjectConfigurations = _invocation.ValidateProjectConfiguration(configuration.Content) };
                    }
                    catch (InvalidTemplateMetadataException exception)
                    {
                        entry = entry with { ProjectConfigurationDiagnostic = exception.Message };
                        if (evaluation.Status == TemplateConstraintEvaluationStatus.Eligible)
                        {
                            evaluation = evaluation with { Status = TemplateConstraintEvaluationStatus.Failed };
                        }
                    }
                }
                else if (entry.Type == TemplateType.Project && evaluation.Status == TemplateConstraintEvaluationStatus.Eligible)
                {
                    evaluation = new(TemplateConstraintEvaluationStatus.NotEvaluated,
                        [new("project-configuration", "configuration", TemplateConstraintEvaluationStatus.NotEvaluated,
                            "Project invocation requires qualified mandatory configuration actions and is unavailable in this item-only flow.", "Use the existing project initializer until project-template creation is qualified.")]);
                }

                candidates.Add(new(template, entry with { ConstraintEvaluation = evaluation }, configuration));
            }
            catch (InvalidTemplateMetadataException exception)
            {
                candidates.Add(FailedCandidate(template, entry, exception.Message));
            }
            catch (IOException exception)
            {
                candidates.Add(FailedCandidate(template, entry, $"Template configuration is unreadable: {exception.Message}"));
            }
            catch (UnauthorizedAccessException exception)
            {
                candidates.Add(FailedCandidate(template, entry, $"Template configuration is unreadable: {exception.Message}"));
            }
        }

        _candidates = candidates.AsReadOnly();
        return _candidates;
    }

    public void Dispose() => _session.Dispose();

    private static TemplateInvocationCoordinator ValidateInvocation(TemplateEngineSession session, TemplateInvocationCoordinator invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return invocation.IsBoundTo(session)
            ? invocation
            : throw new ArgumentException("The invocation coordinator must use this template flow's session.", nameof(invocation));
    }

    private static TemplateCatalogEntry Freeze(TemplateCatalogEntry entry) => entry with
    {
        ShortNames = Array.AsReadOnly<string>([.. entry.ShortNames]),
        Parameters = Array.AsReadOnly<TemplateParameterDefinition>(
            [.. entry.Parameters.Select(parameter => parameter with { Choices = Array.AsReadOnly<TemplateParameterChoice>([.. parameter.Choices]) })]),
    };

    private static bool IsEligible(Candidate candidate)
        => candidate.Entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible
            && candidate.Entry.MetadataDiagnostic is null && candidate.Entry.ProjectConfigurationDiagnostic is null && candidate.Configuration is not null;

    private static Candidate FailedCandidate(ITemplateInfo template, TemplateCatalogEntry entry, string message)
        => new(template, entry with
        {
            ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Failed,
                [new("template.json", "configuration", TemplateConstraintEvaluationStatus.Failed, message, "Correct the template configuration and rerun selection.")]),
        }, null);

    private sealed record Candidate(ITemplateInfo Template, TemplateCatalogEntry Entry, TemplateConfigurationSnapshot? Configuration);

    private sealed record MalformedTypeDescriptor(string Identity, IReadOnlyList<string> ShortNames);
}
