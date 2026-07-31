// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Search;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Renders the outputs of <c>func new</c> and <c>func new --list</c>
/// through <see cref="IInteractionService"/>. Keeps user-facing strings out
/// of the orchestrator so messages can evolve without touching the pipeline
/// logic.
/// </summary>
internal sealed class NewCommandRenderer(IInteractionService interaction)
{
    private readonly IInteractionService _interaction =
        interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <summary>
    /// Renders the "stack.runtime not pinned" hint surfaced when the project
    /// has a <c>.func/config.json</c> but no <c>stack.runtime</c> key.
    /// </summary>
    public void RenderMissingStackRuntime()
    {
        _interaction.WriteError("Cannot determine stack for this project.");
        _interaction.WriteLine(l => l
            .Muted("`stack.runtime` is missing from ")
            .Code(".func/config.json")
            .Muted(". Run ")
            .Code("func init")
            .Muted(" first."));
    }

    /// <summary>
    /// Renders the "stack.language missing on a multi-language stack" hint.
    /// </summary>
    public void RenderMissingLanguage(string stack, string projectPath)
    {
        _interaction.WriteError($"Cannot determine language for stack '{stack}' in '{projectPath}'.");
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Code("func init")
            .Muted(" to set up the project. It scaffolds a new one or adopts an existing one."));
    }

    /// <summary>
    /// Renders the "no Functions project resolved" hint for
    /// <c>func new --list</c> (which requires an init'd project).
    /// </summary>
    public void RenderProjectRequired()
    {
        _interaction.WriteError("`func new --list` needs a Functions project.");
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Code("func init")
            .Muted(" first to choose a stack and language."));
    }

    /// <summary>
    /// Renders the plain-text catalogue for <c>func new --list</c>.
    /// Surfaces an empty header for the resolved stack so the integration
    /// path is exercised when no engines are registered; the populated
    /// catalogue lands once engines exist.
    /// </summary>
    public void RenderCatalogue(string stack, string? language, IReadOnlyList<FunctionTemplateInfo> templates)
    {
        string header = string.IsNullOrWhiteSpace(language)
            ? $"Templates for stack: {stack}"
            : $"Templates for stack: {stack}  (language: {language})";
        _interaction.WriteSectionHeader(header);

        if (templates.Count == 0)
        {
            _interaction.WriteHint("No templates are installed for this stack.");
            _interaction.WriteLine(l => l
                .Muted("Install a template package with ")
                .Code("func new --install <package>")
                .Muted(", then re-run."));
            return;
        }

        string[] columns = ["NAME", "TEMPLATE ID", "DESCRIPTION"];
        IEnumerable<string[]> rows = templates.Select(t => new[]
        {
            string.IsNullOrWhiteSpace(t.DisplayName) ? t.Id : t.DisplayName,
            t.Id,
            t.Description ?? string.Empty,
        });

        _interaction.WriteTable(columns, rows);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Muted("Create one with: ")
            .Code("func new --template <TEMPLATE_ID> --name <function-name>")
            .Muted("."));
    }

    /// <summary>
    /// Renders the <c>func new --list --output json</c> envelope (single
    /// object, not NDJSON — list is a finite, ordered query). Shape:
    /// <c>{ stack, language, templates: [...] }</c>. Each template entry
    /// carries the public-facing fields plus the per-prompt option schema
    /// so tooling can build forms / autocompletion without re-parsing the
    /// workload payload.
    /// </summary>
    public void RenderCatalogueJson(string stack, string? language, IReadOnlyList<FunctionTemplateInfo> templates)
    {
        var envelope = new
        {
            stack,
            language,
            templates = templates.Select(t => new
            {
                id = t.Id,
                displayName = t.DisplayName,
                description = t.Description,
                defaultFunctionName = t.DefaultFunctionName,
                languages = t.Languages,
                requiresExtensionBundle = t.Metadata.RequiresExtensionBundle,
                minBundleVersion = t.Metadata.MinBundleVersion,
                options = t.Metadata.UserPrompts.Select(p => new
                {
                    id = p.Id,
                    description = p.Description,
                    dataType = p.DataType,
                    defaultValue = p.DefaultValue,
                    choices = p.Choices,
                    isRequired = p.IsRequired,
                }),
            }),
        };

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        string json = System.Text.Json.JsonSerializer.Serialize(envelope, jsonOptions);
        _interaction.WriteLine(json);
    }

    /// <summary>
    /// Renders a successful scaffold, listing engine-created files under
    /// <c>Created:</c>, post-action-modified files under <c>Modified:</c>, and
    /// any follow-up guidance a post action surfaced (e.g. blueprint
    /// registration steps) after the file lists.
    /// </summary>
    public void RenderCreated(
        FunctionTemplateInfo template,
        string functionName,
        IReadOnlyList<string> created,
        IReadOnlyList<string> modified,
        IReadOnlyList<string> messages)
    {
        _interaction.WriteSuccess($"Created function '{functionName}' from template '{template.Id}'.");

        if (created.Count > 0)
        {
            _interaction.WriteLine(l => l.Muted("Created:"));
            foreach (string file in created)
            {
                _interaction.WriteLine(l => l.Muted("  ").Code(file));
            }
        }

        if (modified.Count > 0)
        {
            _interaction.WriteLine(l => l.Muted("Modified:"));
            foreach (string file in modified)
            {
                _interaction.WriteLine(l => l.Muted("  ").Code(file));
            }
        }

        if (messages.Count > 0)
        {
            _interaction.WriteBlankLine();
            foreach (string message in messages)
            {
                _interaction.WriteLine(message);
            }
        }
    }

    /// <summary>
    /// Renders the <c>func new --output json</c> envelope for a successful
    /// scaffold: <c>{ template, functionName, created: [...], modified: [...],
    /// messages: [...] }</c>.
    /// </summary>
    public void RenderCreatedJson(
        FunctionTemplateInfo template,
        string functionName,
        IReadOnlyList<string> created,
        IReadOnlyList<string> modified,
        IReadOnlyList<string> messages)
    {
        _interaction.WriteJson(new
        {
            template = template.Id,
            functionName,
            created,
            modified,
            messages,
        });
    }

    /// <summary>
    /// Renders the "files already exist" error and the <c>--force</c> hint for
    /// a create-flow conflict.
    /// </summary>
    public void RenderAlreadyExists(IReadOnlyList<string> existingFiles)
    {
        _interaction.WriteError("Some files already exist:");
        foreach (string file in existingFiles)
        {
            _interaction.WriteLine(l => l.Muted("  ").Code(file));
        }

        _interaction.WriteLine(l => l
            .Muted("Re-run with ")
            .Code("--force")
            .Muted(" to overwrite."));
    }

    /// <summary>
    /// Renders a typed scaffolding failure with the appropriate hint.
    /// </summary>
    public void RenderApplyFailure(TemplateApplicationFailure failure)
    {
        switch (failure)
        {
            case TemplateApplicationFailure.MissingExtensionBundle bundle:
                RenderMissingExtensionBundle(bundle.Stack, bundle.SuggestedBundleId);
                break;
            case TemplateApplicationFailure.WriteFailed write:
                _interaction.WriteError($"Failed to write '{write.Path}': {write.Message}");
                break;
            case TemplateApplicationFailure.InvalidPrompt prompt:
                _interaction.WriteError($"Invalid value for '{prompt.PromptId}': {prompt.Reason}");
                break;
            case TemplateApplicationFailure.ProviderError provider:
                _interaction.WriteError(provider.Message);
                break;
            default:
                _interaction.WriteError("Template scaffolding failed.");
                break;
        }
    }

    /// <summary>
    /// Renders the hard error for a project that mandates an extension bundle
    /// the CLI cannot resolve at all.
    /// </summary>
    public void RenderMissingExtensionBundle(string stack, string suggestedBundleId)
    {
        _interaction.WriteError("This project requires an extension bundle, but none could be resolved.");
        _interaction.WriteLine(l => l
            .Muted("Declare an ")
            .Code("extensionBundle")
            .Muted(" (id ")
            .Code(suggestedBundleId)
            .Muted(") in ")
            .Code("host.json")
            .Muted(", or run ")
            .Code("func setup")
            .Muted(" to install one."));
    }

    /// <summary>
    /// Renders the "template not found" error and the catalog hint for an
    /// unmatched <c>--template</c> id.
    /// </summary>
    public void RenderUnknownTemplate(string requestedTemplate)
    {
        _interaction.WriteError($"Template '{requestedTemplate}' was not found for this project's stack.");
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Code("func new --list")
            .Muted(" to see available templates."));
    }

    /// <summary>
    /// Renders a restricted-template error: the template exists but an
    /// unsatisfied constraint hides it, so surface the reason and the
    /// call-to-action instead of a bare "unknown template".
    /// </summary>
    public void RenderRestrictedTemplate(string requestedTemplate, string reason)
    {
        _interaction.WriteError($"Template '{requestedTemplate}' is not available for this project.");
        _interaction.WriteLine(reason);
    }

    /// <summary>
    /// Renders the "no --template supplied, and cannot prompt" error.
    /// </summary>
    public void RenderTemplateRequired()
    {
        _interaction.WriteError("Missing required option: --template.");
        _interaction.WriteLine(l => l
            .Muted("Pass ")
            .Code("--template <id>")
            .Muted(" or run interactively to pick one. ")
            .Code("func new --list")
            .Muted(" shows available templates."));
    }

    /// <summary>
    /// Renders the "no templates installed for this stack" error and the
    /// install hint (<c>func new</c> never auto-installs).
    /// </summary>
    public void RenderNoTemplatesInstalled(string stack)
    {
        _interaction.WriteError($"No templates are installed for stack '{stack}'.");
        _interaction.WriteLine(l => l
            .Muted("Install a template package with ")
            .Code("func new --install <package>")
            .Muted(", then re-run."));
    }

    /// <summary>
    /// Renders the outcome of a <c>func new --install</c>.
    /// </summary>
    public void RenderInstallResult(TemplatePackageInstallResult result)
    {
        switch (result)
        {
            case TemplatePackageInstallResult.Installed installed:
                _interaction.WriteSuccess(
                    $"Installed template package '{installed.Package.Identifier}' version '{installed.Package.Version}'.");
                break;
            case TemplatePackageInstallResult.AlreadyInstalled already:
                _interaction.WriteWarning(
                    $"Template package '{already.Package.Identifier}' version '{already.Package.Version}' is already installed.");
                break;
            case TemplatePackageInstallResult.NotFound notFound:
                _interaction.WriteError(notFound.Version is null
                    ? $"No template package '{notFound.PackageIdentifier}' was found on the configured source."
                    : $"No template package '{notFound.PackageIdentifier}' version '{notFound.Version}' was found on the configured source.");
                break;
            case TemplatePackageInstallResult.Failed failed:
                _interaction.WriteError($"Failed to install template package: {failed.Message}");
                break;
            default:
                _interaction.WriteError("Template package install returned an unknown result.");
                break;
        }
    }

    /// <summary>
    /// Renders the outcome of a <c>func new --uninstall</c>.
    /// </summary>
    public void RenderUninstallResult(TemplatePackageUninstallResult result)
    {
        switch (result)
        {
            case TemplatePackageUninstallResult.Uninstalled uninstalled:
                _interaction.WriteSuccess($"Uninstalled template package '{uninstalled.PackageId}'.");
                break;
            case TemplatePackageUninstallResult.NotInstalled notInstalled:
                _interaction.WriteWarning($"Template package '{notInstalled.PackageId}' is not installed; nothing to do.");
                break;
            case TemplatePackageUninstallResult.Failed failed:
                _interaction.WriteError($"Failed to uninstall template package: {failed.Message}");
                break;
            default:
                _interaction.WriteError("Template package uninstall returned an unknown result.");
                break;
        }
    }

    /// <summary>
    /// Renders the outcome of a <c>func new --update</c>.
    /// </summary>
    public void RenderUpdateResult(TemplatePackageUpdateResult result)
    {
        switch (result)
        {
            case TemplatePackageUpdateResult.Updated updated:
                _interaction.WriteSuccess($"Updated {updated.Packages.Count} template package(s).");
                foreach (TemplatePackageUpdate package in updated.Packages)
                {
                    _interaction.WriteLine(l => l
                        .Muted("  ")
                        .Code(package.PackageId)
                        .Muted($"  {package.PreviousVersion} -> {package.NewVersion}"));
                }

                break;
            case TemplatePackageUpdateResult.NoUpdatesAvailable:
                _interaction.WriteSuccess("All template packages are up to date.");
                break;
            case TemplatePackageUpdateResult.NotInstalled notInstalled:
                _interaction.WriteWarning($"Template package '{notInstalled.PackageId}' is not installed; nothing to update.");
                break;
            case TemplatePackageUpdateResult.Failed failed:
                _interaction.WriteError($"Failed to update template package(s): {failed.Message}");
                break;
            default:
                _interaction.WriteError("Template package update returned an unknown result.");
                break;
        }
    }

    /// <summary>
    /// Renders <c>func new --search</c> results: one row per matched package
    /// with its version, matched template names, stack/language tags, and an
    /// installed-state annotation that distinguishes not-installed, installed,
    /// and update-available packages.
    /// </summary>
    public void RenderSearchResults(FuncSearchResults results)
    {
        ArgumentNullException.ThrowIfNull(results);

        _interaction.WriteSectionHeader(SearchHeader(results));

        if (results.Packages.Count == 0)
        {
            _interaction.WriteHint(string.IsNullOrWhiteSpace(results.Term)
                ? "The search index contains no template packages."
                : $"No template packages matched '{results.Term}'.");
            _interaction.WriteLine(l => l
                .Muted("Try a different term, or install a known package directly with ")
                .Code("func new --install <package>")
                .Muted("."));
            return;
        }

        string[] columns = ["PACKAGE", "VERSION", "TEMPLATES", "TAGS", "STATUS"];
        IEnumerable<string[]> rows = results.Packages.Select(p => new[]
        {
            p.PackageId,
            p.Version ?? string.Empty,
            FormatTemplateNames(p.Templates),
            FormatTags(p.Templates),
            FormatInstalledState(p.Installed),
        });

        _interaction.WriteTable(columns, rows);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Muted("Install one with: ")
            .Code("func new --install <PACKAGE>")
            .Muted("."));
    }

    private static string SearchHeader(FuncSearchResults results)
    {
        if (!string.IsNullOrWhiteSpace(results.Source))
        {
            return string.IsNullOrWhiteSpace(results.Term)
                ? $"Template packages on feed: {results.Source}"
                : $"Template packages matching '{results.Term}' on feed: {results.Source}";
        }

        return string.IsNullOrWhiteSpace(results.Term)
            ? "Template packages in the search index"
            : $"Template packages matching '{results.Term}'";
    }

    private static string FormatTemplateNames(IReadOnlyList<FuncSearchTemplateResult> templates)
    {
        if (templates.Count == 0)
        {
            return "—";
        }

        const int maxShown = 3;
        IEnumerable<string> names = templates
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string shown = string.Join(", ", names.Take(maxShown));
        int extra = names.Count() - maxShown;
        return extra > 0 ? $"{shown} (+{extra} more)" : shown;
    }

    private static string FormatTags(IReadOnlyList<FuncSearchTemplateResult> templates)
    {
        IEnumerable<string> tags = templates
            .SelectMany(t => new[] { t.Stack, t.Language })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tags.Any() ? string.Join(" · ", tags) : "—";
    }

    private static string FormatInstalledState(FuncTemplateInstalledState state)
        => state switch
        {
            FuncTemplateInstalledState.UpdateAvailable u => $"installed {u.InstalledVersion} · update available {u.AvailableVersion}",
            FuncTemplateInstalledState.Installed i => $"installed {i.Version}",
            _ => "not installed",
        };
}
