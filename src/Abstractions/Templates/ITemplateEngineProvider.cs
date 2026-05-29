// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Provider abstraction implemented once per CLI-internal template engine
/// (V2, DotNet). Each implementation knows how to enumerate and apply
/// templates whose workload payload matches its engine zone.
///
/// Engine providers are <strong>stack-agnostic</strong>: dispatch is keyed by
/// <see cref="EngineId"/>, not by stack. The orchestrator decides which
/// provider to invoke from <see cref="FunctionTemplateInfo.EngineId"/> on each
/// surfaced template.
/// </summary>
/// <remarks>
/// Implementations are registered in DI from their own CLI-internal csproj
/// (<c>Templates.V2</c>, <c>Templates.DotNet</c>) — never from a templates
/// workload. Templates workloads are <c>kind: content</c> and contribute
/// payload only.
/// </remarks>
public interface ITemplateEngineProvider
{
    /// <summary>
    /// Stable engine identifier — one of <see cref="EngineIds.V2"/> or
    /// <see cref="EngineIds.DotNet"/>. Never user-visible.
    /// </summary>
    public string EngineId { get; }

    /// <summary>
    /// Enumerates every template this engine can scaffold from the catalog
    /// snapshots the orchestrator has produced from installed templates
    /// content workloads. Implementations must only return templates that
    /// match <see cref="TemplateListContext.Stack"/>. The orchestrator
    /// post-filters by language for the rendered catalogue.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders and writes the chosen template into the working directory.
    /// Implementations must not throw for the failure modes named in
    /// <see cref="TemplateApplicationFailure"/>; return
    /// <see cref="TemplateApplicationResult.Failed"/> instead so the runner
    /// can dispatch on the specific failure kind.
    /// </summary>
    /// <param name="context">
    /// Resolved invocation context (template, function name, language, working
    /// directory, force flag).
    /// </param>
    /// <param name="parseResult">
    /// The stage-B parse result, after per-template options have been hydrated
    /// and re-parsed. Engines read template-specific option values from this.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> or <paramref name="parseResult"/> is null.
    /// </exception>
    public Task<TemplateApplicationResult> ApplyAsync(
        NewContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken);
}
