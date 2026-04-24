// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Provides function templates for a specific worker runtime. Implementations
/// are registered via DI and consumed by <c>func new</c>.
///
/// A provider may also contribute additional <see cref="Option"/>s to the new
/// command via <see cref="GetNewOptions"/>.
/// </summary>
public interface ITemplateProvider
{
    /// <summary>The canonical worker runtime id this provider owns (e.g. "dotnet").</summary>
    public string WorkerRuntime { get; }

    /// <summary>
    /// Returns true if this provider should handle the given <paramref name="workerRuntime"/>.
    /// </summary>
    public bool CanHandle(string workerRuntime);

    /// <summary>Options this provider contributes to the <c>func new</c> command.</summary>
    public IReadOnlyList<Option> GetNewOptions();

    /// <summary>Enumerates the templates this provider exposes.</summary>
    public Task<IReadOnlyList<FunctionTemplate>> GetTemplatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes the selected template into the target project.
    /// </summary>
    /// <remarks>
    /// Design note: <see cref="FunctionProjectContext"/> exists so providers
    /// don't each re-implement project-on-disk discovery, and so this signature
    /// can grow without churn. An alternative is to drop the type entirely and
    /// have providers read everything they need from <paramref name="parseResult"/>;
    /// we may revisit if the context never accumulates more than CLI inputs.
    /// </remarks>
    /// <param name="project">The target project this scaffold runs against.</param>
    /// <param name="templateName">The selected template id (resolved by <c>func new</c> after any prompting).</param>
    /// <param name="functionName">The function name the user chose (resolved by <c>func new</c> after any prompting).</param>
    /// <param name="parseResult">The full parse result, for reading provider-contributed options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ScaffoldAsync(
        FunctionProjectContext project,
        string templateName,
        string functionName,
        ParseResult parseResult,
        CancellationToken cancellationToken = default);
}
