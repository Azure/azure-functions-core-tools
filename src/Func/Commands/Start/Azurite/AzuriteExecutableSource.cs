// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Where the CLI located an Azurite executable.
/// </summary>
internal enum AzuriteExecutableSource
{
    /// <summary>The executable came from <c>&lt;projectRoot&gt;/node_modules/.bin</c>.</summary>
    ProjectLocal,

    /// <summary>The executable came from a <c>PATH</c> entry.</summary>
    Path,
}
