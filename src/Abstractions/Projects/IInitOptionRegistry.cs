// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Coordinates the options that <see cref="IProjectInitializer"/> implementations contribute
/// to <c>func init</c>. Two installed workloads may contribute an option with the same name
/// (e.g. <c>--no-bundle</c>); the registry returns one shared canonical instance so the option
/// is shown once in <c>--help</c> and every contributing workload reads the same parsed value.
/// </summary>
public interface IInitOptionRegistry
{
    /// <summary>
    /// Returns the canonical option for <paramref name="option"/>'s name. If a same-named option
    /// has already been registered, the supplied instance is discarded and the existing one is
    /// returned (provided the types match). Otherwise, the supplied instance is registered and
    /// returned as-is.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two workloads register the same option name with mismatched types, or when the
    /// supplied option's name or any alias collides with a different existing option.
    /// </exception>
    public Option<T> GetOrAdd<T>(Option<T> option);
}
