// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Azure.Functions.Cli.Workloads;

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

    /// <summary>Display labels for the languages this initializer supports (e.g. "C#", "F#").</summary>
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Options this initializer contributes to the <c>func init</c> command.
    /// Attached during command construction and visible in <c>--help</c>.
    /// </summary>
    public IReadOnlyList<Option> GetInitOptions();

    /// <summary>
    /// Scaffolds a new project at <see cref="WorkloadContext.WorkingDirectory"/>.
    /// </summary>
    public Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default);
}
