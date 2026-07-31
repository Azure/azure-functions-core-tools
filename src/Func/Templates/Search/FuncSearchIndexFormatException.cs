// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Raised when the template search index cannot be parsed: unsupported schema
/// version, missing required properties, or otherwise malformed JSON. The
/// command boundary surfaces this as a user-facing, actionable error.
/// </summary>
internal sealed class FuncSearchIndexFormatException : Exception
{
    public FuncSearchIndexFormatException(string message)
        : base(message)
    {
    }

    public FuncSearchIndexFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
