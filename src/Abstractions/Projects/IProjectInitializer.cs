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
    public IProjectStack Stack { get; }

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
    /// Scaffolds a new project at <see cref="InitContext.WorkingDirectory"/>.
    /// </summary>
    /// <param name="context">The init context.</param>
    /// <param name="parseResult">
    /// The typed parse of the stack init options, from which this initializer reads the values of the options it
    /// returned from <see cref="GetInitOptions"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel initialization.</param>
    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs stack-owned, non-scaffolding setup after an installed project template has created
    /// the project at <see cref="WorkloadContext.WorkingDirectory"/> and the <c>.func/config.json</c>
    /// write has been attempted. Implementations must not create or overwrite template content; project
    /// files are owned by the template. The default implementation does nothing.
    /// </summary>
    public Task ConfigureProjectAsync(InitContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
