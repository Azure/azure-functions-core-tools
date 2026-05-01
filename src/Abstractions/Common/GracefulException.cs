// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Represents a user-friendly exception that should be displayed without a stack trace.
/// </summary>
public class GracefulException : Exception
{
    public bool IsUserError { get; }
    public string? VerboseMessage { get; }

    public GracefulException(string message, bool isUserError = false, string? verboseMessage = null)
        : base(message)
    {
        IsUserError = isUserError;
        VerboseMessage = verboseMessage;
    }

    public GracefulException(string message, string verboseMessage)
        : base(message)
    {
        IsUserError = true;
        VerboseMessage = verboseMessage;
    }
}

/// <summary>
/// Thrown when a command or file is not found.
/// </summary>
public class CommandUnknownException : GracefulException
{
    public string CommandName { get; }

    public CommandUnknownException(string commandName)
        : base($"Unknown command: '{commandName}'", isUserError: true)
    {
        CommandName = commandName;
    }
}

/// <summary>
/// Thrown when command arguments fail to parse.
/// </summary>
public class CommandParsingException : GracefulException
{
    public CommandParsingException(string message)
        : base(message, isUserError: true)
    {
    }
}
