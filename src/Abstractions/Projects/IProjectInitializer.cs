// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;

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
