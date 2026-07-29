// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Coordinates the options that <see cref="IStartHostOptionContributor"/> implementations
/// contribute to <c>func start</c>/<c>func run</c>. Adds each unique option to the supplied
/// <see cref="Command"/> exactly once and returns a shared canonical instance across workloads
/// that contribute the same name, so a shared option shows once in <c>--help</c> and every
/// contributor reads the same parsed value.
/// </summary>
/// <remarks>
/// Public so workload test projects can drive <see cref="IStartHostOptionContributor.GetStartOptions"/>
/// without taking a dependency on the host's internals.
/// </remarks>
public sealed class StartOptionRegistry
{
    private readonly Command _command;
    private readonly Dictionary<string, Entry> _byName = new(StringComparer.Ordinal);
    private string _activeStack = "?";

    public StartOptionRegistry(Command command)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Sets the workload id used when reporting collisions, so the error message can name both
    /// the workload registering now and the one that owned the conflicting name first.
    /// </summary>
    public void SetActiveStack(string stack)
    {
        _activeStack = stack ?? "?";
    }

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
    public Option<T> GetOrAdd<T>(Option<T> option)
    {
        ArgumentNullException.ThrowIfNull(option);

        if (_byName.TryGetValue(option.Name, out Entry existing))
        {
            if (existing.Option is not Option<T> typed)
            {
                throw new InvalidOperationException(
                    $"Workload '{_activeStack}' contributes option '{option.Name}' as {typeof(T).Name}, " +
                    $"but workload '{existing.OwningStack}' already registered it as {existing.Option.ValueType.Name}. " +
                    "Same-named options across workloads must use the same value type.");
            }

            return typed;
        }

        foreach (string alias in option.Aliases)
        {
            if (_byName.TryGetValue(alias, out Entry existingByAlias))
            {
                throw new InvalidOperationException(
                    $"Workload '{_activeStack}' contributes option '{option.Name}' with alias '{alias}', " +
                    $"but workload '{existingByAlias.OwningStack}' already uses '{alias}' for option '{existingByAlias.Option.Name}'.");
            }
        }

        _command.Options.Add(option);
        _byName[option.Name] = new Entry(option, _activeStack);
        foreach (string alias in option.Aliases)
        {
            _byName[alias] = new Entry(option, _activeStack);
        }

        return option;
    }

    private readonly record struct Entry(Option Option, string OwningStack);
}
