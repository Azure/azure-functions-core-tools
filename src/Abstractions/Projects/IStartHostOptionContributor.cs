// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Contributes stack-specific options to <c>func start</c>/<c>func run</c> and translates the
/// parsed values into host run adjustments (environment variables, JSON output capture).
/// Implementations are registered via DI and consumed by the start command.
///
/// The options an implementation registers via <see cref="GetStartOptions"/> appear in
/// <c>--help</c> only while the owning workload is installed. Their parsed values are read back
/// inside <see cref="Configure"/>, and the resulting <see cref="StartHostConfiguration"/> is
/// applied only when the resolved project's stack equals <see cref="Stack"/>.
/// </summary>
public interface IStartHostOptionContributor
{
    /// <summary>The canonical stack id this contributor owns (e.g. "dotnet").</summary>
    public string Stack { get; }

    /// <summary>
    /// Registers the options this contributor adds to the start command via
    /// <paramref name="registry"/>, and returns the canonical instances to use when reading
    /// values back inside <see cref="Configure"/>.
    /// </summary>
    public IReadOnlyList<Option> GetStartOptions(StartOptionRegistry registry);

    /// <summary>
    /// Translates the parsed options into a <see cref="StartHostConfiguration"/>. Returns
    /// <see cref="StartHostConfiguration.Empty"/> when none of the contributor's options are set.
    /// </summary>
    public StartHostConfiguration Configure(ParseResult parseResult);
}
