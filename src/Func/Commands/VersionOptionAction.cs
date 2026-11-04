// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Routes the root version option through the CLI's semantic-version renderer.
/// </summary>
internal sealed class VersionOptionAction(VersionCommand versionCommand) : SynchronousCommandLineAction
{
    private readonly VersionCommand _versionCommand = versionCommand ?? throw new ArgumentNullException(nameof(versionCommand));

    // Preserve the built-in version action's short-circuit behavior for invalid arguments.
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult) => _versionCommand.WriteShortVersion();
}
