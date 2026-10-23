// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartInitRequest(
    InitExecutionRequest Init,
    string? QuickstartId = null,
    bool AuthorizeAcquisition = false,
    IReadOnlySet<string>? NativeStacks = null,
    bool OfferBasicSelection = false);

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
    private const string Basic = "Basic Functions app";
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
            || request.QuickstartId is not null && string.IsNullOrWhiteSpace(request.QuickstartId)
            || input.ExplicitStack is not null && string.IsNullOrWhiteSpace(input.ExplicitStack)
            || input.ExplicitLanguage is not null && string.IsNullOrWhiteSpace(input.ExplicitLanguage))
        {
            return InitExecutionResult.Reject("invalidSelection", "Supply nonempty --name, --template and --quickstart values when specified.");
        }
        if (request.AuthorizeAcquisition && request.QuickstartId is null)
        {
            return InitExecutionResult.Reject("invalidAuthorization", "--acquire-quickstart requires an explicit --quickstart catalog ID.");
        }
        if (input.Force)
        {
            return InitExecutionResult.Reject("forceNotQualified", "Project-template init does not support --force. Nothing was deleted; choose a new empty target.");
        }
        if (request.QuickstartId is not null && input.TemplateReference is not null)
        {
            return InitExecutionResult.Reject("selectionConflict", "Choose either an installed --template or a discovery --quickstart, not both.");
        }
        string? targetDiagnostic = _targets.GetDiagnostic(input.WorkingDirectory);
        if (targetDiagnostic is not null)
        {
            if (request.OfferBasicSelection)
            {
                return InitExecutionResult.ContinueWithLegacyInitializer();
            }
            return InitExecutionResult.Reject("unsupportedTarget", $"Project-template init requires an empty target. {targetDiagnostic}");
        }
        bool interactive = !input.NonInteractive && _interaction.IsInteractive;
        if (!request.OfferBasicSelection && request.QuickstartId is null && input.TemplateReference is null)
        {
            return InitExecutionResult.Reject("missingSelection",
                "Select an installed project with --template or a catalog template with --quickstart before using template-backed init options.");
        }
        if (request.QuickstartId is not null)
        {
            return await ExecuteQuickstartSelectionAsync(request, interactive, cancellationToken);
        }
        if (input.TemplateReference is not null)
        {
            return await ExecuteInstalledTemplateSelectionAsync(input, interactive, cancellationToken);
        }

        QualifiedQuickstart? qualified = null;
        NativeTemplateSelection? installedSelection = null;
        BundledBasicSelection? bundledSelection = null;
        string? installedIdentity = null;
        string stackName;
        OwnedStackCandidate? selectedOwnerCandidate = null;
        QuickstartInstalledQuery query;
        // No native reader, prepared payload or engine handle survives this selection phase.
        BoundInitScope scope;
        try
        {
            scope = await _scopes.CreateAsync(cancellationToken);
        }
        catch (ConflictingProjectStackMetadataException) when (request.OfferBasicSelection)
        {
            return InitExecutionResult.ContinueWithLegacyInitializer();
        }
        using (scope)
        {
            IReadOnlySet<string>? nativeStacks = request.NativeStacks;
            IReadOnlySet<string>? ownedStacks = request.OfferBasicSelection
                ? scope.StackOwners.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;
            if (request.OfferBasicSelection && (nativeStacks is null || !scope.Catalog.Stacks.Any(stack =>
                nativeStacks.Contains(stack.Stack) && ownedStacks!.Contains(stack.Stack))))
            {
                return InitExecutionResult.ContinueWithLegacyInitializer();
            }
            (IProjectStack? Stack, bool Canceled) stackSelection = await SelectStackAsync(scope.Catalog, input.ExplicitStack, interactive,
                nativeStacks, ownedStacks, request.OfferBasicSelection, cancellationToken);
            if (request.OfferBasicSelection && stackSelection.Canceled)
            {
                return Canceled();
            }
            IProjectStack? stack = stackSelection.Stack;
            if (stack is null)
            {
                if (request.OfferBasicSelection)
                {
                    return InitExecutionResult.ContinueWithLegacyInitializer();
                }
                return InitExecutionResult.Reject("missingStack", "Select an installed stack with --stack, or cancel project-template selection without changes.");
            }
            stackName = stack.Stack;
            if (!scope.StackOwners.TryGetValue(stackName, out WorkloadEntry? owner))
            {
                return InitExecutionResult.Reject("ownerUnavailable", "The selected stack has no unique actual loaded runtime owner. Repair its workload before selecting a project template.");
            }
            selectedOwnerCandidate = new(stack, owner);
            query = new(input.WorkingDirectory, NativeTemplateOwnerIdentity.FromEntry(owner));
            if (input.ExplicitStack is not null && input.ExplicitLanguage is not null
                && new InstalledProjectStackCatalog([stack]).GetCanonicalLanguage(input.ExplicitLanguage) is null)
            {
                if (request.OfferBasicSelection)
                {
                    return InitExecutionResult.ContinueWithLegacyInitializer();
                }
                return InitExecutionResult.Reject("invalidLanguage", "The requested language is not supported by the selected installed stack.");
            }
            QuickstartCatalogEntry? nomination = null;
            if (installedIdentity is null)
            {
                if (!interactive)
                {
                    return InitExecutionResult.Reject("missingSelection",
                        "Without interactive input, supply --template <installed-full-identity> or --quickstart <catalog-id>. Discovery never auto-selects a result.");
                }
                IReadOnlyList<InstalledTemplateArtifact> installed;
                try
                {
                    using Templater flow = await _templates.CreateProjectAsync(new(input.WorkingDirectory) { RequireCleanStore = true },
                        scope.Availability, scope.Support, cancellationToken);
                    installed = await flow.ProjectArtifactsAsync(scope.Verifier, query.Owner, query.Channel, cancellationToken, scope.BundledVerifier);
                }
                catch (TemplateStoreRecoveryRequiredException) when (request.OfferBasicSelection)
                {
                    _interaction.WriteWarning("Installed template choices are unavailable while template-store recovery is pending. Continuing with the existing init initializer.");
                    return InitExecutionResult.ContinueWithLegacyInitializer();
                }
                Dictionary<string, InstalledTemplateArtifact> choices = [];
                foreach (InstalledTemplateArtifact item in installed)
                {
                    if (request.OfferBasicSelection && item.Entry.IsHidden)
                    {
                        continue;
                    }
                    if (item.Diagnostic is not null)
                    {
                        _interaction.WriteWarning($"{item.Entry.Identity}: {item.Diagnostic}");
                    }
                    if (request.OfferBasicSelection && item.Diagnostic is not null)
                    {
                        continue;
                    }

                    string context = item.Entry.ProjectConfigurations is { Count: > 0 } declarations
                        ? $"projects {string.Join(", ", declarations.Select(declaration => $"{declaration.Stack}/{declaration.Language}"))}"
                        : $"language {item.Entry.Language ?? "unspecified"}";
                    string binding = item.Selection is { } selection
                        ? $"bound to {selection.Owner.PackageId} {selection.Owner.Version}"
                        : item.BundledSelection is { } bundled
                        ? $"bundled by {bundled.Owner.PackageId} {bundled.Owner.Version}"
                        : "native binding not proven";
                    string package = item.Entry.Package is { } packageInfo
                        ? $"package {packageInfo.Identifier}"
                        : "package unavailable";
                    choices.Add($"{item.Entry.Name} ({item.Entry.Identity}; {context}; {package}; {binding})", item);
                }
                List<string> menu = [];
                if (request.OfferBasicSelection)
                {
                    menu.Add(Basic);
                }
                menu.AddRange(choices.Keys);
                menu.AddRange([Browse, Cancel]);
                while (installedIdentity is null && nomination is null)
                {
                    string choice = await _interaction.PromptForSelectionAsync(
                        $"Select a project template for {stack.DisplayName} ({stackName}):", menu, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.OfferBasicSelection && choice == Basic)
                    {
                        return InitExecutionResult.ContinueWithBasicProject(stackName);
                    }
                    if (choices.TryGetValue(choice, out InstalledTemplateArtifact? selected))
                    {
                        installedIdentity = selected.Entry.Identity;
                        installedSelection = selected.Selection;
                        bundledSelection = selected.BundledSelection;
                    }
                    else if (choice == Browse)
                    {
                        QuickstartBrowseResult browsing = await _browser.BrowseAsync(new(stackName, input.ExplicitLanguage)
                        {
                            AllowSelection = true,
                            InstalledQuery = query,
                        }, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (browsing.Outcome == QuickstartBrowseOutcome.Back)
                        {
                            continue;
                        }
                        if (browsing.Outcome != QuickstartBrowseOutcome.Selected || browsing.Entry is null)
                        {
                            return Canceled();
                        }
                        nomination = browsing.Entry;
                    }
                    else
                    {
                        return Canceled();
                    }
                }
            }
            if (installedIdentity is null)
            {
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
        if (qualified is not null)
        {
            return await ExecuteQualifiedQuickstartAsync(request,
                new(selectedOwnerCandidate!.Stack, qualified), interactive, cancellationToken);
        }
        InitExecutionRequest execution = input with
        {
            TemplateReference = installedSelection?.TemplateIdentity ?? installedIdentity,
            ArtifactBinding = installedSelection,
            BundledBinding = bundledSelection,
            RefreshCapabilities = true,
            ConfirmCreation = interactive,
        };
        cancellationToken.ThrowIfCancellationRequested();
        return await _init.ExecuteAsync(execution, new([]), cancellationToken);
    }

    private async Task<(IProjectStack? Stack, bool Canceled)> SelectStackAsync(InstalledProjectStackCatalog catalog, string? requested,
        bool interactive, IReadOnlySet<string>? nativeStacks, IReadOnlySet<string>? ownedStacks, bool useSoleDefault,
        CancellationToken cancellationToken)
    {
        if (requested is not null)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return (null, false);
            }
            IProjectStack? found = catalog.FindStack(requested);
            return (found is not null && (nativeStacks is null || nativeStacks.Contains(found.Stack))
                && (ownedStacks is null || ownedStacks.Contains(found.Stack)) ? found : null, false);
        }
        IProjectStack[] available = [.. catalog.Stacks.Where(stack =>
            (nativeStacks is null || nativeStacks.Contains(stack.Stack))
            && (ownedStacks is null || ownedStacks.Contains(stack.Stack)))];
        if (!interactive || available.Length == 0)
        {
            return (null, false);
        }
        if (useSoleDefault && available.Length == 1)
        {
            return (available[0], false);
        }
        Dictionary<string, IProjectStack> choices = available.ToDictionary(stack => $"{stack.DisplayName} ({stack.Stack})", StringComparer.Ordinal);
        string selected = await _interaction.PromptForSelectionAsync("Select a project stack:", choices.Keys.Append(Cancel), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return selected == Cancel ? (null, true) : (choices.GetValueOrDefault(selected), false);
    }

    private async Task<InitExecutionResult> ExecuteQuickstartSelectionAsync(QuickstartInitRequest request, bool interactive,
        CancellationToken cancellationToken)
    {
        QuickstartCatalog catalog = await _catalog.SearchAsync(new(), cancellationToken);
        _catalogRenderer.WriteProvenance(catalog.Provenance);
        QuickstartCatalogEntry[] nominations = [.. catalog.Entries.Where(entry => entry.Entry.Id == request.QuickstartId)];
        if (nominations.Length != 1)
        {
            return InitExecutionResult.Reject("ambiguousQuickstart",
                nominations.Length == 0
                    ? $"Quickstart '{request.QuickstartId}' was not found in the discovery catalog."
                    : "Supply one exact, unique discovery --quickstart ID.");
        }

        QuickstartCatalogEntry nomination = nominations[0];
        _catalogRenderer.WriteDetails(nomination, nomination.Entry.Language);
        QualifiedQuickstartCandidate[] candidates;
        List<string> diagnostics = [];
        using (BoundInitScope scope = await _scopes.CreateAsync(cancellationToken))
        {
            InitExecutionResult? stackError = GetOwnedStackCandidates(scope, request.Init, out List<OwnedStackCandidate> ownedStacks,
                out string? canonicalLanguage);
            if (stackError is not null)
            {
                return stackError;
            }

            using Templater templates = await _templates.CreateProjectAsync(new(request.Init.WorkingDirectory) { RequireCleanStore = true },
                scope.Availability, scope.Support, cancellationToken);
            List<QualifiedQuickstartCandidate> qualified = [];
            foreach (OwnedStackCandidate candidate in ownedStacks)
            {
                QuickstartInstalledQuery query = new(request.Init.WorkingDirectory, NativeTemplateOwnerIdentity.FromEntry(candidate.Owner));
                try
                {
                    QualifiedQuickstart result = await _qualification.ResolveAsync(nomination, query, candidate.Owner, cancellationToken);
                    IReadOnlyList<InstalledTemplateArtifact> installed = await templates.ProjectArtifactsAsync(scope.Verifier,
                        query.Owner, query.Channel, cancellationToken, scope.BundledVerifier);
                    foreach (InstalledTemplateArtifact artifact in installed)
                    {
                        QuickstartInstalledTemplates.ThrowSystemicFailure(artifact.Failure);
                    }
                    InstalledTemplateArtifact[] exact = [.. installed.Where(artifact =>
                        QuickstartInstalledTemplates.MatchesInstalledCoordinate(artifact, nomination.PackageReference!))];
                    if (exact.Length > 0 && (exact.Length != 1
                        || !QuickstartInstalledTemplates.IsExactDuplicate(nomination, exact[0], query.Owner, query.Channel)
                        || !QuickstartInstalledTemplates.SupportsTemplateConfiguration(exact[0].Entry, scope.Catalog,
                            candidate.Stack, canonicalLanguage)))
                    {
                        diagnostics.Add($"{candidate.Stack.Stack}: {exact[0].Diagnostic
                            ?? "the installed nominated project has no unique exact binding with compatible project declarations."}");
                        continue;
                    }
                    qualified.Add(new(candidate.Stack, result));
                }
                catch (BasicAcquisitionException exception) when (QuickstartQualification.IsCandidateRefusal(exception.Failure))
                {
                    diagnostics.Add($"{candidate.Stack.Stack}: {exception.Message}");
                }
                catch (BasicAcquisitionException exception)
                {
                    return InitExecutionResult.Reject("qualificationUnavailable", exception.Message);
                }
            }
            candidates = [.. qualified];
        }

        if (candidates.Length == 0)
        {
            string details = diagnostics.Count == 0 ? "No uniquely owned installed stack supports the selected quickstart."
                : string.Join(" ", diagnostics);
            return InitExecutionResult.Reject(diagnostics.Count > 0 ? "qualificationUnavailable" : "quickstartNotQualified",
                $"Quickstart '{request.QuickstartId}' has no independently qualified installed stack candidate. {details}");
        }

        QualifiedQuickstartCandidate selected;
        if (candidates.Length == 1)
        {
            selected = candidates[0];
        }
        else
        {
            if (!interactive)
            {
                return InitExecutionResult.Reject("ambiguousQuickstartStack",
                    $"Quickstart '{request.QuickstartId}' has multiple qualified installed stack owners. Supply --stack: {string.Join(", ", candidates.Select(candidate => candidate.Stack.Stack))}.");
            }

            Dictionary<string, QualifiedQuickstartCandidate> choices = candidates.ToDictionary(
                candidate => $"{candidate.Stack.DisplayName} ({candidate.Stack.Stack}; {candidate.Qualified.Selection.Owner.PackageId} {candidate.Qualified.Selection.Owner.Version})",
                StringComparer.Ordinal);
            string choice = await _interaction.PromptForSelectionAsync("Select the qualified project stack:", [.. choices.Keys, Cancel],
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!choices.TryGetValue(choice, out selected!))
            {
                return Canceled();
            }
        }

        return await ExecuteQualifiedQuickstartAsync(request, selected, interactive, cancellationToken);
    }

    private async Task<InitExecutionResult> ExecuteInstalledTemplateSelectionAsync(InitExecutionRequest input, bool interactive,
        CancellationToken cancellationToken)
    {
        InstalledTemplateCandidate[] candidates;
        bool exactIdentityFound = false;
        List<string> diagnostics = [];
        using (BoundInitScope scope = await _scopes.CreateAsync(cancellationToken))
        {
            InitExecutionResult? stackError = GetOwnedStackCandidates(scope, input, out List<OwnedStackCandidate> ownedStacks,
                out string? canonicalLanguage);
            if (stackError is not null)
            {
                return stackError;
            }

            using Templater templates = await _templates.CreateProjectAsync(new(input.WorkingDirectory) { RequireCleanStore = true },
                scope.Availability, scope.Support, cancellationToken);
            List<InstalledTemplateCandidate> selected = [];
            foreach (OwnedStackCandidate candidate in ownedStacks)
            {
                IReadOnlyList<InstalledTemplateArtifact> installed = await templates.ProjectArtifactsAsync(scope.Verifier,
                    NativeTemplateOwnerIdentity.FromEntry(candidate.Owner), null, cancellationToken, scope.BundledVerifier);
                InstalledTemplateArtifact[] exact = [.. installed.Where(item => string.Equals(item.Entry.Identity, input.TemplateReference, StringComparison.Ordinal))];
                if (exact.Length == 0)
                {
                    continue;
                }
                exactIdentityFound = true;
                if (exact.Length != 1)
                {
                    diagnostics.Add($"{candidate.Stack.Stack}: the installed identity is duplicated.");
                    continue;
                }

                InstalledTemplateArtifact artifact = exact[0];
                if (artifact.Failure is IOException ioFailure)
                {
                    throw ioFailure;
                }
                if (artifact.Failure is UnauthorizedAccessException accessFailure)
                {
                    throw accessFailure;
                }
                var ownerIdentity = NativeTemplateOwnerIdentity.FromEntry(candidate.Owner);
                bool externallyBound = artifact.Selection is { } external && external.Owner == ownerIdentity;
                bool bundledBound = artifact.BundledSelection is { } bundled && bundled.Owner == ownerIdentity;
                if (artifact.Diagnostic is not null || externallyBound == bundledBound
                    || !QuickstartInstalledTemplates.SupportsTemplateConfiguration(artifact.Entry, scope.Catalog,
                        candidate.Stack, canonicalLanguage))
                {
                    diagnostics.Add($"{candidate.Stack.Stack}: {artifact.Diagnostic ?? "the installed project declarations do not match its owner stack and language capabilities."}");
                    continue;
                }

                selected.Add(new(candidate.Stack, candidate.Owner, artifact));
            }
            candidates = [.. selected];
        }

        if (candidates.Length == 0)
        {
            return InitExecutionResult.Reject(exactIdentityFound ? "templateNotQualified" : "templateNotInstalled",
                exactIdentityFound
                    ? $"Installed template '{input.TemplateReference}' has no uniquely owner-bound compatible project candidate. {string.Join(" ", diagnostics)}"
                    : $"Template '{input.TemplateReference}' is not installed. Use 'func new install <package>' to install it explicitly; --template never triggers acquisition.");
        }

        InstalledTemplateCandidate selectedCandidate;
        if (candidates.Length == 1)
        {
            selectedCandidate = candidates[0];
        }
        else
        {
            if (!interactive)
            {
                return InitExecutionResult.Reject("ambiguousTemplateStack",
                    $"Template '{input.TemplateReference}' has multiple qualified installed stack owners. Supply --stack: {string.Join(", ", candidates.Select(candidate => candidate.Stack.Stack))}.");
            }

            Dictionary<string, InstalledTemplateCandidate> choices = candidates.ToDictionary(
                candidate => $"{candidate.Stack.DisplayName} ({candidate.Stack.Stack}; {candidate.Owner.PackageId} {candidate.Owner.PackageVersion})",
                StringComparer.Ordinal);
            string choice = await _interaction.PromptForSelectionAsync("Select the installed template's project stack:", [.. choices.Keys, Cancel],
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!choices.TryGetValue(choice, out selectedCandidate!))
            {
                return Canceled();
            }
        }

        InstalledTemplateArtifact binding = selectedCandidate.Artifact;
        InitExecutionRequest execution = input with
        {
            ExplicitStack = input.ExplicitStack ?? selectedCandidate.Stack.Stack,
            ArtifactBinding = binding.Selection,
            BundledBinding = binding.BundledSelection,
            RefreshCapabilities = true,
            ConfirmCreation = interactive,
        };
        return await _init.ExecuteAsync(execution, new([]), cancellationToken);
    }

    private InitExecutionResult? GetOwnedStackCandidates(BoundInitScope scope, InitExecutionRequest input,
        out List<OwnedStackCandidate> candidates, out string? canonicalLanguage)
    {
        candidates = [];
        canonicalLanguage = null;
        IProjectStack[] available;
        if (input.ExplicitStack is not null)
        {
            IProjectStack? requested = scope.Catalog.FindStack(input.ExplicitStack);
            if (requested is null)
            {
                return InitExecutionResult.Reject("invalidStack",
                    $"Stack '{input.ExplicitStack}' must identify one installed stack. Supported stacks: {string.Join(", ", scope.Catalog.Stacks.Select(stack => stack.Stack))}.");
            }
            if (!scope.StackOwners.TryGetValue(requested.Stack, out WorkloadEntry? owner))
            {
                return InitExecutionResult.Reject("ownerUnavailable",
                    $"Stack '{requested.Stack}' has no unique actual loaded runtime owner. Repair its workload before selecting a project template.");
            }
            available = [requested];
            candidates.Add(new(requested, owner));
        }
        else
        {
            available = [.. scope.Catalog.Stacks];
            foreach (IProjectStack stack in available)
            {
                if (scope.StackOwners.TryGetValue(stack.Stack, out WorkloadEntry? owner))
                {
                    candidates.Add(new(stack, owner));
                }
            }
        }

        if (input.ExplicitLanguage is not null)
        {
            canonicalLanguage = scope.Catalog.GetCanonicalLanguage(input.ExplicitLanguage);
            if (canonicalLanguage is null)
            {
                return InitExecutionResult.Reject("invalidLanguage",
                    $"Language '{input.ExplicitLanguage}' is not supported by an installed stack.");
            }
            if (input.ExplicitStack is not null && !available[0].SupportedLanguages.Contains(canonicalLanguage, StringComparer.OrdinalIgnoreCase))
            {
                return InitExecutionResult.Reject("invalidLanguage",
                    $"Language '{input.ExplicitLanguage}' is not supported by stack '{available[0].Stack}'.");
            }
            List<OwnedStackCandidate> matching = [];
            foreach (OwnedStackCandidate candidate in candidates)
            {
                if (candidate.Stack.SupportedLanguages.Contains(canonicalLanguage, StringComparer.OrdinalIgnoreCase))
                {
                    matching.Add(candidate);
                }
            }
            candidates = matching;
        }

        if (candidates.Count == 0)
        {
            return InitExecutionResult.Reject("noCompatibleStack",
                input.ExplicitLanguage is null
                    ? "No uniquely owned installed stack is available for project-template selection."
                    : $"No uniquely owned installed stack supports language '{input.ExplicitLanguage}'.");
        }

        return null;
    }


    private async Task<InitExecutionResult> ExecuteQualifiedQuickstartAsync(QuickstartInitRequest request,
        QualifiedQuickstartCandidate candidate, bool interactive, CancellationToken cancellationToken)
    {
        InitExecutionRequest input = request.Init;
        QualifiedQuickstart qualified = candidate.Qualified;
        string stackName = candidate.Stack.Stack;
        WritePlan(qualified, input, stackName);
        if (input.DryRun)
        {
            return InitExecutionResult.Reject("payloadRequired",
                "Metadata intent only. Dry-run did not acquire, register, recover or clean anything. An engine preview requires the exact installed payload; use --template after explicit acquisition.");
        }
        if (!interactive && !request.AuthorizeAcquisition)
        {
            return InitExecutionResult.Reject("acquisitionAuthorizationRequired",
                "This exact acquisition plan is not authorized. In scripts, select it with --quickstart.");
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
        if (acquired.Status == BasicAcquisitionStatus.Canceled)
        {
            string completed = acquired.Completed.Count == 0 ? string.Empty
                : $" Verified completed packages remain: {string.Join(", ", acquired.Completed.Select(step => $"{step.PackageId}@{step.Version}"))}.";
            _interaction.WriteWarning($"{acquired.Message}{completed}"
                + (acquired.Failure is { } failure ? $" Acquisition diagnostic: {failure}." : string.Empty));
            throw new OperationCanceledException(acquired.Message, acquired.Cause, cancellationToken);
        }
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

        InitExecutionRequest execution = input with
        {
            ExplicitStack = input.ExplicitStack ?? stackName,
            TemplateReference = qualified.Selection.TemplateIdentity,
            ArtifactBinding = qualified.Selection,
            RefreshCapabilities = true,
            ConfirmCreation = interactive,
        };
        cancellationToken.ThrowIfCancellationRequested();
        return await _init.ExecuteAsync(execution, new([]), cancellationToken);
    }

    private void WritePlan(QualifiedQuickstart qualified, InitExecutionRequest input, string stackName)
    {
        _interaction.WriteSectionHeader("Independently qualified acquisition plan");
        _interaction.WriteLine($"Selected project stack: {stackName}");
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
            BasicOwnedTemplateUpdate? update = approval.UpdateFor(artifact.PackageId);
            if (update is not null)
            {
                _interaction.WriteLine($"Owned package update: {update.Before.Receipt.Version} -> {artifact.Version.ToNormalizedString()}");
                _interaction.WriteLine($"Existing package SHA256: {update.Before.Receipt.Sha256}; receipt SHA256: {update.Before.ReceiptSha256}");
            }
            _interaction.WriteLine($"Required package: {artifact.PackageId} {artifact.Version.ToNormalizedString()} from {artifact.Source}");
            _interaction.WriteLine($"SHA256: {artifact.Sha256}; content revision: {artifact.ContentRevision}");
            foreach (BasicTemplateDefinition template in artifact.Templates)
            {
                _interaction.WriteLine($"Approved inventory: {template.Identity} ({template.Type}) at {template.Path}");
            }
        }
        _interaction.WriteWarning("Remote project and workload declarations remain unverified claims. Only the exact listed owned package updates are authorized. No workload upgrade is authorized. Native generation and required CLI configuration are previewed after exact acquisition, before interactive creation consent.");
    }

    private static InitExecutionResult Canceled() => InitExecutionResult.Reject("canceled", "Project template selection canceled before acquisition or project writes.");

    private sealed record OwnedStackCandidate(IProjectStack Stack, WorkloadEntry Owner);

    private sealed record QualifiedQuickstartCandidate(IProjectStack Stack, QualifiedQuickstart Qualified);

    private sealed record InstalledTemplateCandidate(IProjectStack Stack, WorkloadEntry Owner, InstalledTemplateArtifact Artifact);
}
