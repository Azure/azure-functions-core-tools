// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;

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
    /// Renders the "no template providers registered" hint surfaced when no
    /// engine project has been wired into DI yet. Terminal output of both
    /// commands until engine-driven output paths replace it.
    /// </summary>
    public void RenderNoEnginesRegistered()
    {
        _interaction.WriteError("No templates available.");
        _interaction.WriteLine(l => l
            .Muted("Install a templates workload: ")
            .Code("func workload install Azure.Functions.Cli.Workloads.Templates.<stack>")
            .Muted("."));
    }

    /// <summary>
    /// Renders the warning surfaced when the project's bundle channel
    /// (preview / experimental) has no matching installed templates workload
    /// and the runner has fallen back to the stable workload (issue #5369).
    /// </summary>
    public void RenderTemplatesChannelFallback(string stack, string bundleId, BundleChannel projectChannel)
    {
        string channelName = projectChannel.ToDisplayString();
        string suggestedPkg = TemplatesWorkloadConstants.GetPackageId(stack);
        string suggestedVer = $"<version>-{channelName}.1";

        _interaction.WriteWarning(
            $"No '{channelName}' templates workload installed for bundle '{bundleId}'; using stable templates instead.");
        _interaction.WriteLine(l => l
            .Muted("Templates may differ from what your bundle ships. Install the matching workload with: ")
            .Code($"func workload install {suggestedPkg} --version {suggestedVer}")
            .Muted("."));
    }

    /// <summary>
    /// Renders the "no installed templates workload for this stack" hint
    /// surfaced when engine providers exist but no <c>Workloads.Templates.&lt;stack&gt;</c>
    /// pkg is installed. Fires whenever the registry walker comes back empty
    /// for the active stack.
    /// </summary>
    public void RenderNoTemplatesWorkloadInstalled(string stack)
    {
        string suggested = TemplatesWorkloadConstants.GetPackageId(stack);
        _interaction.WriteError($"No templates workload installed for stack '{stack}'.");
        _interaction.WriteLine(l => l
            .Muted("Install one with: ")
            .Code($"func workload install {suggested}")
            .Muted("."));
    }

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
            _interaction.WriteHint("(no templates registered yet — install a templates workload)");
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
                engineId = t.EngineId,
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
}
