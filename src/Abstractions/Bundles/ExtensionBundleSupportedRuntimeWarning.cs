// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>

/// Non-fatal warning that the project's worker runtime is not in the profile's <c>supportedRuntimes</c>.

/// </summary>
public sealed record ExtensionBundleSupportedRuntimeWarning(
    string WorkerRuntime,
    IReadOnlyList<string> SupportedRuntimes,
    string ProfileName);
