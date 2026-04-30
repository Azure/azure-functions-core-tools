// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Parser-independent description of an option accepted by a <see cref="FuncCommand"/>.
/// Use <see cref="FuncCommandOption{T}"/> to declare options of a specific value type;
/// instances are kept by reference (identity) and used as the lookup key when reading
/// parsed values from <see cref="FuncCommandInvocationContext"/>.
/// </summary>
public abstract class FuncCommandOption
{
    /// <param name="name">Long form (e.g. <c>"--name"</c>). Required.</param>
    /// <param name="shortName">Optional short form (e.g. <c>"-n"</c>). Pass <c>null</c> for none.</param>
    /// <param name="description">Help text shown in <c>--help</c> output.</param>
    private protected FuncCommandOption(string name, string? shortName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);

        Name = name;
        ShortName = shortName;
        Description = description;
    }

    /// <summary>Long form (e.g. <c>"--name"</c>).</summary>
    public string Name { get; }

    /// <summary>Short form (e.g. <c>"-n"</c>), or <c>null</c> if the option has only a long form.</summary>
    public string? ShortName { get; }

    /// <summary>Help text shown in <c>--help</c> output.</summary>
    public string Description { get; }

    /// <summary>Type-erased view of the value type carried by this option (used by the parser adapter to construct a typed parser option).</summary>
    internal abstract Type ValueType { get; }
}

/// <summary>
/// A typed <see cref="FuncCommandOption"/>. Construct with one of the two
/// constructors: omit <c>defaultValue</c> for a non-defaulting option, or pass
/// it to provide an explicit default. The two-constructor shape distinguishes
/// "no default" from "default is <c>default(T)</c>" — for value types like
/// <see langword="int"/>, the unset case is <c>HasDefaultValue == false</c>,
/// not <c>DefaultValue == 0</c>.
/// </summary>
/// <typeparam name="T">Value type read by <see cref="FuncCommandInvocationContext.GetValue{T}(FuncCommandOption{T})"/>.</typeparam>
public sealed class FuncCommandOption<T> : FuncCommandOption
{
    /// <summary>Declares an option without a default value.</summary>
    public FuncCommandOption(string name, string? shortName, string description)
        : base(name, shortName, description)
    {
        HasDefaultValue = false;
        DefaultValue = default!;
    }

    /// <summary>Declares an option with an explicit default value.</summary>
    public FuncCommandOption(string name, string? shortName, string description, T defaultValue)
        : base(name, shortName, description)
    {
        HasDefaultValue = true;
        DefaultValue = defaultValue;
    }

    /// <summary>
    /// <c>true</c> if a default value was supplied at construction. When <c>false</c>,
    /// the value of <see cref="DefaultValue"/> is unspecified and should not be read.
    /// </summary>
    public bool HasDefaultValue { get; }

    /// <summary>
    /// The default value supplied at construction. Read only when
    /// <see cref="HasDefaultValue"/> is <c>true</c>.
    /// </summary>
    public T DefaultValue { get; }

    internal override Type ValueType => typeof(T);
}
