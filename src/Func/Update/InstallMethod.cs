// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Well-known ways the func CLI can end up on a user's machine. The value
/// drives whether <c>func update</c> is allowed to run the in-place pipeline
/// (<see cref="Direct"/>) or must defer to the owning package manager.
/// </summary>
internal enum InstallMethodKind
{
    /// <summary>
    /// The installation is not owned by a known package manager; the in-place
    /// updater is safe to run.
    /// </summary>
    Direct,

    Npm,
    Homebrew,
    Chocolatey,
    Winget,
}

/// <summary>
/// Detected installation footprint for the running CLI. The
/// <see cref="UpgradeCommand"/> is a paste-ready shell command the user
/// should run instead of <c>func update</c> when
/// <see cref="Kind"/> is anything other than <see cref="InstallMethodKind.Direct"/>.
/// </summary>
internal sealed record InstallMethod(
    InstallMethodKind Kind,
    string DisplayName,
    string? UpgradeCommand)
{
    /// <summary>The neutral, in-place install (no package manager detected).</summary>
    public static InstallMethod Direct { get; } = new(InstallMethodKind.Direct, "direct install", null);
}
