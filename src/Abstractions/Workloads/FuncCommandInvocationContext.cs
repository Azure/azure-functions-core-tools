// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Surface a <see cref="FuncCommand"/> uses at execution time to read parsed option
/// and argument values. The host supplies a concrete subclass that bridges to the
/// underlying parser; workload authors only see this abstract type.
///
/// Lookups use reference identity on the descriptor — pass the same
/// <see cref="FuncCommandOption{T}"/> / <see cref="FuncCommandArgument{T}"/> instance
/// that the command declared in <see cref="FuncCommand.Options"/> /
/// <see cref="FuncCommand.Arguments"/>. Two distinct descriptors with identical
/// fields are treated as different keys.
/// </summary>
public abstract class FuncCommandInvocationContext
{
    /// <summary>Reads the parsed value of a typed option.</summary>
    /// <returns>
    /// The parsed value when supplied on the command line, the option's
    /// <see cref="FuncCommandOption{T}.DefaultValue"/> when not supplied and a default
    /// was declared, or <c>default(T)</c> otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">The option was not declared on the
    /// command being executed.</exception>
    public abstract T? GetValue<T>(FuncCommandOption<T> option);

    /// <summary>Reads the parsed value of a typed positional argument.</summary>
    /// <returns>
    /// The parsed value when supplied on the command line, or <c>default(T)</c>
    /// otherwise (including when the argument is optional and was omitted).
    /// </returns>
    /// <exception cref="ArgumentException">The argument was not declared on the
    /// command being executed.</exception>
    public abstract T? GetValue<T>(FuncCommandArgument<T> argument);
}
