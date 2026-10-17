// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Describes how the current CLI installation is managed.
/// </summary>
internal sealed record InstallationMetadata(string InstallMethod, InstallationUpdatePolicy UpdatePolicy);

internal enum InstallationUpdatePolicy
{
    InPlace,
    External,
}
