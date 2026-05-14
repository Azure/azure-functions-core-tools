// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Describes how an initialization step should be presented to the user.
/// </summary>
internal enum StartInitializationDisplayKind
{
    Status,
    Progress,
}
