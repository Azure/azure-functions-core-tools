// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Describes how the current CLI installation is managed.
/// </summary>
internal sealed record InstallationMetadata(InstallationMethod InstallMethod, InstallationUpdatePolicy UpdatePolicy);

internal enum InstallationMethod
{
    Standalone,
    Npm,
    Homebrew,
    Winget,
    Msi,
}

internal enum InstallationUpdatePolicy
{
    InPlace,
    External,
}
