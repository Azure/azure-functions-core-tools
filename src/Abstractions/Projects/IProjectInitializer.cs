// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Scaffolds a new Azure Functions project for a specific stack.
/// Implementations are registered via DI and consumed by <c>func init</c>.
///
/// An initializer may also contribute additional <see cref="Option"/>s to the
/// init command via <see cref="GetInitOptions"/>; the option values are read
/// back inside <see cref="InitializeAsync"/> via the supplied <see cref="ParseResult"/>.
/// </summary>
public interface IProjectInitializer
{
    /// <summary>The canonical stack id this initializer owns (e.g. "dotnet").</summary>
    public string Stack { get; }

    /// <summary>
    /// Additional <c>FUNCTIONS_WORKER_RUNTIME</c> values that should resolve
    /// to this stack. Used during <c>func init</c> adoption to map the
    /// runtime string in <c>local.settings.json</c> (e.g. "dotnet-isolated",
    /// "native") to the canonical <see cref="Stack"/> id. Matching is
    /// case-insensitive. Defaults to an empty list, which means
    /// "<see cref="Stack"/> is the only accepted runtime value".
    /// </summary>
    public IReadOnlyList<string> WorkerRuntimeAliases => [];

    /// <summary>
    /// Human-friendly name for this stack shown in interactive prompts and help text
    /// (e.g. ".NET", "Node.js"). Defaults to <see cref="Stack"/> when not overridden.
    /// </summary>
    public string DisplayName => Stack;

    /// <summary>Display labels for the languages this initializer supports (e.g. "C#", "F#").</summary>
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Maps each canonical language name to its accepted aliases (e.g. "C#" → ["csharp"]).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; }

    /// <summary>
    /// Stack-default validator for function names, applied by <c>func new</c>'s
    /// option hydrator when a template's own function-name prompt declares no
    /// <c>ValidatorRegex</c>. Returning <c>null</c>
    /// means "no stack default — the option accepts any string"; that is the
    /// default and the right answer for stacks whose templates always carry
    /// their own validators. Implementations that supply a regex should match
    /// the canonical identifier rules for the stack's language(s) so a
    /// missing template-level validator still rejects invalid identifiers
    /// before any file is written.
    /// </summary>
    public Regex? DefaultFunctionNameValidator => null;

    /// <summary>
    /// Registers the options this initializer contributes to <c>func init</c> via
    /// <paramref name="registry"/>, and returns the canonical instances to use when reading
    /// values back inside <see cref="InitializeAsync"/>. Options shared across workloads
    /// (e.g. <c>--no-bundles</c>) appear once in <c>--help</c> and resolve to the same instance
    /// for every contributing workload.
    /// </summary>
    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry);

    /// <summary>
    /// Scaffolds a new project at <see cref="WorkloadContext.WorkingDirectory"/>.
    /// </summary>
    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default);
}
