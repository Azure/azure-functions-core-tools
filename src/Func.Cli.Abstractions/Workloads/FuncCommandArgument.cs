// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Parser-independent description of a positional argument accepted by a
/// <see cref="FuncCommand"/>. Use <see cref="FuncCommandArgument{T}"/> to declare
/// arguments of a specific value type; instances are kept by reference (identity)
/// and used as the lookup key when reading parsed values from
/// <see cref="FuncCommandInvocationContext"/>.
/// </summary>
public abstract class FuncCommandArgument
{
    /// <param name="name">Argument name shown in usage / help (e.g. <c>"path"</c>). Required.</param>
    /// <param name="description">Help text shown in <c>--help</c> output.</param>
    /// <param name="isRequired">When <c>true</c>, the argument must be supplied; when <c>false</c>, it is optional.</param>
    private protected FuncCommandArgument(string name, string description, bool isRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);

        Name = name;
        Description = description;
        IsRequired = isRequired;
    }

    /// <summary>Argument name (e.g. <c>"path"</c>).</summary>
    public string Name { get; }

    /// <summary>Help text shown in <c>--help</c> output.</summary>
    public string Description { get; }

    /// <summary>
    /// <c>true</c> if the argument is required (parser arity <c>ExactlyOne</c>);
    /// <c>false</c> if it is optional (<c>ZeroOrOne</c>).
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>The runtime value type carried by this argument (used by the parser adapter).</summary>
    internal abstract Type ValueType { get; }
}

/// <summary>
/// A typed <see cref="FuncCommandArgument"/>. Read parsed values via
/// <see cref="FuncCommandInvocationContext.GetValue{T}(FuncCommandArgument{T})"/>
/// using the same descriptor instance.
/// </summary>
/// <typeparam name="T">Value type read by <see cref="FuncCommandInvocationContext.GetValue{T}(FuncCommandArgument{T})"/>.</typeparam>
public sealed class FuncCommandArgument<T> : FuncCommandArgument
{
    public FuncCommandArgument(string name, string description, bool isRequired = false)
        : base(name, description, isRequired)
    {
    }

    internal override Type ValueType => typeof(T);
}
