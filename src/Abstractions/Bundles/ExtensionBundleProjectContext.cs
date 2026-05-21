// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>

/// Input to <see cref="IExtensionBundleResolver.ResolveAsync"/>.

/// </summary>
public sealed record ExtensionBundleProjectContext(
    string BundleId,
    string HostJsonVersionRange,
    string? WorkerRuntime,
    string? ProfileName,
    string? ProfileBundleVersionRange);
