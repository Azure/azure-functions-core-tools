// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartInitRequest(InitExecutionRequest Init, string? QuickstartId = null, bool AuthorizeAcquisition = false);

internal interface IQuickstartInitCoordinator
{
    public Task<InitExecutionResult> ExecuteAsync(QuickstartInitRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates discovery, exact consent, acquisition and a separate fresh protected init flow.
/// </summary>
internal sealed class QuickstartInitCoordinator(IBoundInitScopeFactory scopes, ITemplaterFactory templates,
    IQuickstartBrowser browser, IQuickstartCatalogService catalog, QuickstartQualification qualification,
    BasicAcquisitionRunner acquisition, IInitExecutionRunner init, InitExecutionTargetInspector targets,
    IInteractionService interaction, QuickstartCatalogRenderer catalogRenderer) : IQuickstartInitCoordinator
{
    private const string Browse = "Browse more templates";
    private const string Cancel = "Cancel";
    private readonly IBoundInitScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    private readonly ITemplaterFactory _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IQuickstartBrowser _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    private readonly IQuickstartCatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly QuickstartQualification _qualification = qualification ?? throw new ArgumentNullException(nameof(qualification));
    private readonly BasicAcquisitionRunner _acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
    private readonly IInitExecutionRunner _init = init ?? throw new ArgumentNullException(nameof(init));
    private readonly InitExecutionTargetInspector _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly QuickstartCatalogRenderer _catalogRenderer = catalogRenderer ?? throw new ArgumentNullException(nameof(catalogRenderer));

    public async Task<InitExecutionResult> ExecuteAsync(QuickstartInitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Init);
        InitExecutionRequest input = request.Init;
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Name is not null && string.IsNullOrWhiteSpace(input.Name)
            || input.TemplateReference is not null && string.IsNullOrWhiteSpace(input.TemplateReference)
            || request.QuickstartId is not null && string.IsNullOrWhiteSpace(request.QuickstartId))
        {
            return InitExecutionResult.Reject("invalidSelection", "Supply nonempty --name, --template and --quickstart values when specified.");
        }
        if (request.AuthorizeAcquisition && request.QuickstartId is null)
        {
            return InitExecutionResult.Reject("invalidAuthorization", "--acquire-quickstart requires an explicit --quickstart catalog ID.");
        }
        if (input.Force)
        {
            return InitExecutionResult.Reject("forceNotQualified", "Guided init does not support --force. Nothing was deleted; choose a new empty target.");
        }
        if (request.QuickstartId is not null && input.TemplateReference is not null)
        {
            return InitExecutionResult.Reject("selectionConflict", "Choose either an installed --template or a discovery --quickstart, not both.");
        }
        string? targetDiagnostic = _targets.GetDiagnostic(input.WorkingDirectory);
        if (targetDiagnostic is not null)
        {
            return InitExecutionResult.Reject("unsupportedTarget", $"Guided init requires an empty target. {targetDiagnostic}");
        }
        bool interactive = !input.NonInteractive && _interaction.IsInteractive;
        QualifiedQuickstart? qualified = null;
        NativeTemplateSelection? installedSelection = null;
        string? installedIdentity = input.TemplateReference;
        string stackName;
        QuickstartInstalledQuery query;
        // No native reader, prepared payload or engine handle survives this selection phase.
        using (BoundInitScope scope = await _scopes.CreateAsync(cancellationToken))
        {
            IProjectStack? stack = await SelectStackAsync(scope.Catalog, input.ExplicitStack, interactive, cancellationToken);
            if (stack is null)
            {
                return InitExecutionResult.Reject("missingStack", "Select an installed stack with --stack, or cancel guided init without changes.");
            }
            stackName = stack.Stack;
            if (!scope.StackOwners.TryGetValue(stackName, out WorkloadEntry? owner))
            {
                return InitExecutionResult.Reject("ownerUnavailable", "The selected stack has no unique actual loaded runtime owner. Repair its workload before guided init.");
            }
            query = new(input.WorkingDirectory, NativeTemplateOwnerIdentity.FromEntry(owner));
            if (input.ExplicitLanguage is not null && new InstalledProjectStackCatalog([stack]).GetCanonicalLanguage(input.ExplicitLanguage) is null)
            {
                return InitExecutionResult.Reject("invalidLanguage", "The requested language is not supported by the selected installed stack.");
            }
            if (installedIdentity is null && request.QuickstartId is null)
            {
                if (!interactive)
                {
                    return InitExecutionResult.Reject("missingSelection",
                        "Without interactive input, supply --template <installed-full-identity> or --quickstart <catalog-id>. Discovery never auto-selects a result.");
                }
                IReadOnlyList<InstalledTemplateArtifact> installed;
                using (Templater flow = await _templates.CreateProjectAsync(new(input.WorkingDirectory) { RequireCleanStore = true },
                    scope.Availability, scope.Support, cancellationToken))
                {
                    installed = await flow.ProjectArtifactsAsync(scope.Verifier, query.Owner, query.Channel, cancellationToken);
                }
                Dictionary<string, InstalledTemplateArtifact> choices = [];
                foreach (InstalledTemplateArtifact item in installed)
                {
                    string proof = item.Selection is null ? "unqualified artifact proof" : "verified artifact";
                    choices.Add($"{choices.Count + 1}. {item.Entry.Name} ({item.Entry.Identity}) [{proof}]", item);
                    if (item.Diagnostic is not null)
                    {
                        _interaction.WriteWarning($"{item.Entry.Identity}: {item.Diagnostic}");
                    }
                }
                string choice = await _interaction.PromptForSelectionAsync("Select an installed project template:", choices.Keys.Concat([Browse, Cancel]), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (choices.TryGetValue(choice, out InstalledTemplateArtifact? selected))
                {
                    installedIdentity = selected.Entry.Identity;
                    installedSelection = selected.Selection;
                }
                else if (choice != Browse)
                {
                    return Canceled();
                }
            }
            if (installedIdentity is null)
            {
                QuickstartCatalogEntry? nomination;
                if (request.QuickstartId is not null)
                {
                    QuickstartCatalog results = await _catalog.SearchAsync(new(LegacyStack: stack), cancellationToken);
                    _catalogRenderer.WriteProvenance(results.Provenance);
                    QuickstartCatalogEntry[] matches = [.. results.Entries.Where(entry => entry.Entry.Id == request.QuickstartId)];
                    if (matches.Length != 1)
                    {
                        return InitExecutionResult.Reject("ambiguousQuickstart", "Supply one exact, unique discovery --quickstart ID for the selected stack.");
                    }
                    nomination = matches[0];
                    _catalogRenderer.WriteDetails(nomination, nomination.Entry.Language);
                }
                else
                {
                    nomination = await _browser.BrowseAsync(new(stackName, input.ExplicitLanguage)
                    {
                        AllowSelection = true,
                        InstalledQuery = query,
                    }, cancellationToken);
                }
                if (nomination is null)
                {
                    return Canceled();
                }
                try
                {
                    qualified = await _qualification.ResolveAsync(nomination, query, owner, cancellationToken);
                }
                catch (BasicAcquisitionException exception)
                {
                    return InitExecutionResult.Reject("qualificationUnavailable", exception.Message);
                }
            }
        }
        InitExecutionRequest execution = input with
        {
            TemplateReference = qualified?.Selection.TemplateIdentity ?? installedIdentity,
            ArtifactBinding = qualified?.Selection ?? installedSelection,
            RefreshCapabilities = true,
            ConfirmCreation = interactive,
        };
        if (qualified is not null)
        {
            WritePlan(qualified, input);
            if (input.DryRun)
            {
                return InitExecutionResult.Reject("payloadRequired",
                    "Metadata intent only. Dry-run did not acquire, register, recover or clean anything. An engine preview requires the exact installed payload; use --template after explicit acquisition.");
            }
            if (!interactive && !request.AuthorizeAcquisition)
            {
                return InitExecutionResult.Reject("acquisitionAuthorizationRequired",
                    "Scripts must explicitly pass --acquire-quickstart with --quickstart to authorize this exact qualified acquisition and creation plan. --guided alone is not consent.");
            }
            if (interactive && !await _interaction.ConfirmAsync("Acquire the exact plan above, then preview project creation?", false, false, cancellationToken))
            {
                return Canceled();
            }
            cancellationToken.ThrowIfCancellationRequested();
            using (BoundInitScope current = await _scopes.CreateAsync(cancellationToken))
            {
                if (!current.StackOwners.TryGetValue(stackName, out WorkloadEntry? currentOwner)
                    || NativeTemplateOwnerIdentity.FromEntry(currentOwner) != qualified.Selection.Owner)
                {
                    return InitExecutionResult.Reject("planChanged", "The actual loaded stack owner changed after consent. Select and approve again.");
                }
            }
            if (_targets.GetDiagnostic(input.WorkingDirectory) is { } changedTarget)
            {
                return InitExecutionResult.Reject("targetChanged", $"The target changed after consent. No payload was acquired. {changedTarget}");
            }
            BasicAcquisitionResult acquired = await _acquisition.RunAsync(qualified.Acquisition, cancellationToken);
            if (acquired.Status != BasicAcquisitionStatus.Completed || acquired.Failure is not null
                || acquired.Revision != qualified.Acquisition.Approval!.Plan.Revision
                || acquired.Source != qualified.Selection.Source
                || !acquired.Completed.Any(step => string.Equals(step.PackageId, qualified.Acquisition.StackPackageId, StringComparison.OrdinalIgnoreCase)
                    && step.Version == qualified.Acquisition.StackVersion.ToNormalizedString())
                || qualified.Acquisition.Approval.Artifacts.Any(artifact => !acquired.Completed.Any(step =>
                    string.Equals(step.PackageId, artifact.PackageId, StringComparison.OrdinalIgnoreCase)
                    && step.Version == artifact.Version.ToNormalizedString())))
            {
                return InitExecutionResult.Reject("acquisitionFailed",
                    $"Acquisition ended with {acquired.Status} ({acquired.Failure}). {acquired.Message}",
                    $"Completed packages remain: {string.Join(", ", acquired.Completed.Select(step => $"{step.PackageId} {step.Version}"))}. No project creation was attempted; no rollback is claimed.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await _init.ExecuteAsync(execution, new([]), cancellationToken);
    }

    private async Task<IProjectStack?> SelectStackAsync(InstalledProjectStackCatalog catalog, string? requested,
        bool interactive, CancellationToken cancellationToken)
    {
        if (requested is not null)
        {
            return string.IsNullOrWhiteSpace(requested) ? null : catalog.FindStack(requested);
        }
        if (!interactive || catalog.Stacks.Count == 0)
        {
            return null;
        }
        var choices = catalog.Stacks.ToDictionary(stack => $"{stack.DisplayName} ({stack.Stack})", StringComparer.Ordinal);
        string selected = await _interaction.PromptForSelectionAsync("Select a project stack:", choices.Keys.Append(Cancel), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return choices.GetValueOrDefault(selected);
    }

    private void WritePlan(QualifiedQuickstart qualified, InitExecutionRequest input)
    {
        _interaction.WriteSectionHeader("Independently qualified acquisition plan");
        _interaction.WriteLine($"Template: {qualified.Selection.TemplateIdentity}");
        _interaction.WriteLine($"Target: {input.WorkingDirectory.Info.FullName}");
        _interaction.WriteLine($"Name: {input.Name ?? "(template default)"}; language filter: {input.ExplicitLanguage ?? "all"}");
        NativeTemplateOwnerIdentity owner = qualified.Selection.Owner;
        _interaction.WriteLine($"Actual stack owner: {owner.PackageId} {owner.Version} from {owner.Source}; {owner.Kind}; RID {owner.RuntimeIdentifier ?? "any"}; entry {owner.AssemblyPath} {owner.EntryPointType}");
        _interaction.WriteLine($"Logical owner: {owner.LogicalPackageId ?? owner.PackageId} {owner.LogicalVersion ?? owner.Version} from {owner.LogicalSource ?? owner.Source}");
        BasicAcquisitionApproval approval = qualified.Acquisition.Approval!;
        _interaction.WriteLine($"Bundled approval revision: {approval.Plan.Revision}; host: {qualified.Acquisition.NativeContext!.HostIdentifier} {qualified.Acquisition.NativeContext.EngineHostVersion}");
        foreach (BasicTemplateArtifact artifact in approval.Artifacts)
        {
            _interaction.WriteLine($"Required package: {artifact.PackageId} {artifact.Version.ToNormalizedString()} from {artifact.Source}");
            _interaction.WriteLine($"SHA256: {artifact.Sha256}; content revision: {artifact.ContentRevision}");
            foreach (BasicTemplateDefinition template in artifact.Templates)
            {
                _interaction.WriteLine($"Approved inventory: {template.Identity} ({template.Type}) at {template.Path}");
            }
        }
        _interaction.WriteWarning("Remote project and workload declarations remain unverified claims. Native generation and required CLI configuration are previewed after exact acquisition, before interactive creation consent. No feed, pin or workload upgrade is authorized.");
    }

    private static InitExecutionResult Canceled() => InitExecutionResult.Reject("canceled", "Guided init canceled before acquisition or project writes.");
}
