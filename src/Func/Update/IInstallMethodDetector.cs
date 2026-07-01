// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Classifies the current CLI installation as either a direct/in-place
/// install (safe to update in place) or one managed by a known package
/// manager (must defer to that tool). Behind an interface so
/// <see cref="Commands.Update.UpdateCommand"/> can be unit-tested without
/// depending on the running process's real path.
/// </summary>
internal interface IInstallMethodDetector
{
    /// <summary>
    /// Returns the detected install method. Never returns <see langword="null"/>;
    /// unknown installations map to <see cref="InstallMethod.Direct"/>.
    /// </summary>
    public InstallMethod Detect();
}
